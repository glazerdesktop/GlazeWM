use anyhow::Context;
use tracing::info;

use crate::{
  containers::{
    commands::{move_container_within_tree, replace_container},
    traits::CommonGetters,
    WindowContainer,
  },
  user_config::UserConfig,
  windows::{traits::WindowGetters, WindowState},
  wm_state::WmState,
};

/// Updates the state of a window.
///
/// Adds the window for redraw if there is a state change.
pub fn update_window_state(
  window: WindowContainer,
  target_state: WindowState,
  state: &mut WmState,
  config: &UserConfig,
) -> anyhow::Result<()> {
  if window.state() == target_state {
    return Ok(());
  }

  info!("Updating window state: {:?}.", target_state);

  match target_state {
    WindowState::Tiling => set_tiling(window, state, config),
    _ => set_non_tiling(window, target_state, state),
  }
}

/// Updates the state of a window to be `WindowState::Tiling`.
fn set_tiling(
  window: WindowContainer,
  state: &mut WmState,
  config: &UserConfig,
) -> anyhow::Result<()> {
  if let WindowContainer::NonTilingWindow(window) = window {
    let workspace =
      window.workspace().context("Window has no workspace.")?;

    // Get the position in the tree to insert the new tiling window. This
    // will be the window's previous tiling position if it has one, or
    // instead beside the last focused tiling window in the workspace.
    let (target_parent, target_index) = window
      .insertion_target()
      // Check whether insertion target is still valid.
      .filter(|(insertion_target, _)| {
        insertion_target
          .workspace()
          .map(|workspace| workspace.is_displayed())
          .unwrap_or(false)
      })
      // Fallback to the last focused tiling window within the workspace.
      .or_else(|| {
        let focused_window = workspace
          .descendant_focus_order()
          .filter(|c| c.is_tiling_window())
          .next()?;

        Some((focused_window.parent()?, focused_window.index() + 1))
      })
      // Default to inserting at the end of the workspace.
      .unwrap_or((workspace.clone().into(), workspace.child_count()));

    let tiling_window =
      window.to_tiling(config.value.gaps.inner_gap.clone());

    // Replace the original window with the created tiling window.
    replace_container(
      tiling_window.clone().into(),
      window.parent().context("No parent")?,
      window.index(),
    )?;

    move_container_within_tree(
      tiling_window.clone().into(),
      target_parent.clone(),
      Some(target_index),
      state,
    )?;

    state
      .pending_sync
      .containers_to_redraw
      .extend(target_parent.tiling_children().map(Into::into))
  }

  Ok(())
}

/// Updates the state of a window to be either `WindowState::Floating`,
/// `WindowState::Fullscreen`, or `WindowState::Minimized`.
fn set_non_tiling(
  window: WindowContainer,
  target_state: WindowState,
  state: &mut WmState,
) -> anyhow::Result<()> {
  // A window can only be updated to a minimized state if it is
  // natively minimized.
  if target_state == WindowState::Minimized
    && !window.native().is_minimized()?
  {
    info!("No window state update. Minimizing window.");
    return window.native().minimize();
  }

  match window {
    WindowContainer::NonTilingWindow(window) => {
      let current_state = window.state();

      // Update the window's previous state if the discriminant changes.
      if std::mem::discriminant(&current_state)
        != std::mem::discriminant(&target_state)
      {
        window.set_prev_state(current_state);
      }

      window.set_state(target_state);
      state.pending_sync.containers_to_redraw.push(window.into())
    }
    WindowContainer::TilingWindow(window) => {
      let parent = window.parent().context("No parent")?;
      let workspace = window.workspace().context("No workspace.")?;

      let insertion_target = (parent.clone(), window.index());
      let non_tiling_window =
        window.to_non_tiling(target_state.clone(), Some(insertion_target));

      // Non-tiling windows should always be direct children of the
      // workspace.
      if parent != workspace.clone().into() {
        move_container_within_tree(
          window.clone().into(),
          workspace.clone().into(),
          Some(workspace.child_count()),
          state,
        )?;
      }

      replace_container(
        non_tiling_window.clone().into(),
        workspace.clone().into(),
        window.index(),
      )?;

      let changed_containers = std::iter::once(non_tiling_window.into())
        .chain(workspace.tiling_children().map(Into::into));

      state
        .pending_sync
        .containers_to_redraw
        .extend(changed_containers)
    }
  }

  Ok(())
}

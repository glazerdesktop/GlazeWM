use anyhow::Context;
use tracing::info;

use crate::{
  common::{
    events::handle_window_moved_end::window_moved_end,
    platform::NativeWindow, LengthValue,
  },
  containers::{
    traits::{CommonGetters, PositionGetters},
    WindowContainer,
  },
  user_config::UserConfig,
  windows::{
    commands::resize_window, traits::WindowGetters, TilingWindow,
  },
  wm_state::WmState,
};

/// Handles the event for when a window is finished being moved or resized
/// by the user (e.g. via the window's drag handles).
///
/// This resizes the window if it's a tiling window.
pub fn handle_window_moved_or_resized_end(
  native_window: NativeWindow,
  state: &mut WmState,
  config: &UserConfig,
) -> anyhow::Result<()> {
  let found_window = state.window_from_native(&native_window);
  if let Some(window) = found_window {
    // TODO: Log window details.

    let parent = window.parent().context("No parent.")?;

    // Snap window to its original position if it's the only window in the
    // workspace.
    if parent.is_workspace() && window.tiling_siblings().count() == 0 {
      state.pending_sync.containers_to_redraw.push(window.into());
      return Ok(());
    }

    let new_rect = window.native().refresh_frame_position()?;
    let old_rect = window.to_rect()?;

    let width_delta = new_rect.width() - old_rect.width();
    let height_delta = new_rect.height() - old_rect.height();

    if let WindowContainer::NonTilingWindow(window) = window {
      let has_window_moved = match (width_delta, height_delta) {
        (0, 0) => true,
        _ => false,
      };

      if has_window_moved {
        window_moved_end(window, state, config)?;
      }
    } else if let WindowContainer::TilingWindow(window) = window {
      window_resized_end(window, state, width_delta, height_delta)?;
    }
  }

  Ok(())
}

/// Handles window resize events
fn window_resized_end(
  window: TilingWindow,
  state: &mut WmState,
  width_delta: i32,
  height_delta: i32,
) -> anyhow::Result<()> {
  info!("Tiling window resized");
  resize_window(
    window.clone().into(),
    Some(LengthValue::from_px(width_delta)),
    Some(LengthValue::from_px(height_delta)),
    state,
  )
}
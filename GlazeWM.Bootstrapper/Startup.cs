using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reactive.Linq;
using System.Windows.Forms;
using GlazeWM.Bar;
using GlazeWM.Domain.Common.Commands;
using GlazeWM.Domain.Containers.Commands;
using GlazeWM.Domain.UserConfigs;
using GlazeWM.Domain.UserConfigs.Commands;
using GlazeWM.Domain.Windows;
using GlazeWM.Domain.Windows.Commands;
using GlazeWM.Infrastructure;
using GlazeWM.Infrastructure.Bussing;
using GlazeWM.Infrastructure.Common.Commands;
using GlazeWM.Infrastructure.Common.Events;
using GlazeWM.Infrastructure.WindowsApi;
using static GlazeWM.Infrastructure.WindowsApi.WindowsApiService;

namespace GlazeWM.Bootstrapper
{
  internal sealed class Startup
  {
    private readonly BarService _barService;
    private readonly Bus _bus;
    private readonly KeybindingService _keybindingService;
    private readonly SystemEventService _systemEventService;
    private readonly WindowEventService _windowEventService;
    private readonly WindowService _windowService;

    private SystemTrayIcon _systemTrayIcon { get; set; }

    public Startup(
      BarService barService,
      Bus bus,
      KeybindingService keybindingService,
      SystemEventService systemEventService,
      WindowEventService windowEventService,
      WindowService windowService)
    {
      _barService = barService;
      _bus = bus;
      _keybindingService = keybindingService;
      _systemEventService = systemEventService;
      _windowEventService = windowEventService;
      _windowService = windowService;
    }

    public void Run()
    {
      try
      {
        // Set the process-default DPI awareness.
        _ = SetProcessDpiAwarenessContext(DpiAwarenessContext.PerMonitorAwareV2);

        _bus.Events.OfType<ApplicationExitingEvent>()
          .Subscribe(_ => OnApplicationExit());

        // Launch bar WPF application. Spawns bar window when monitors are added, so the service needs
        // to be initialized before populating initial state.
        _barService.StartApp();

        // Populate initial monitors, windows, workspaces and user config.
        _bus.Invoke(new PopulateInitialStateCommand());

        // Listen on registered keybindings.
        _keybindingService.Start();

        // Listen for window events (eg. close, focus).
        _windowEventService.Start();

        // Listen for system-related events (eg. changes to display settings).
        _systemEventService.Start();

        var systemTrayIconConfig = new SystemTrayIconConfig
        {
          HoverText = "GlazeWM",
          IconResourceName = "GlazeWM.Bootstrapper.icon.ico",
          Actions = new Dictionary<string, Action>
          {
            { "Reload config", () => _bus.Invoke(new ReloadUserConfigCommand()) },
            { "Exit", () => _bus.Invoke(new ExitApplicationCommand(false)) },
          }
        };

        // Add application to system tray.
        _systemTrayIcon = new SystemTrayIcon(systemTrayIconConfig);
        _systemTrayIcon.Show();

        var focusedWindows = new List<IntPtr>();

        MouseEvents.MouseMoves.Subscribe((@event) =>
        {
          // Returns window underneath cursor.  This could be a child window or parent.
          var windowHandle = WindowFromPoint(@event.pt);

          // TODO: Remove debug logs.
          Debug.WriteLine($"coord window class: {WindowService.GetClassNameOfHandle(windowHandle)}");
          Debug.WriteLine($"coord window process: {WindowService.GetProcessOfHandle(windowHandle)?.ProcessName}");

          // If the mouse is hovering over the currently focused main window or one of it's children, do nothing.
          if (focusedWindows.Contains(windowHandle))
            return;

          // If the FocusedWindows list didn't contain the window, this must be a new window being focused.
          focusedWindows.Clear();
          focusedWindows.Add(windowHandle);

          // Check if the window is the main window or a child window.
          var parentWindow = GetParent(windowHandle);

          // Walk the window up each parent window until you have the main window.
          while (parentWindow != IntPtr.Zero)
          {
            windowHandle = parentWindow;
            focusedWindows.Add(windowHandle);
            parentWindow = GetParent(windowHandle);
          }

          var foundWindow = _windowService
            .GetWindows()
            .FirstOrDefault(window => window.Handle == windowHandle);

          // TODO: Remove debug logs.
          Debug.WriteLine($"found window class: {foundWindow?.ClassName}");
          Debug.WriteLine($"found window process: {foundWindow?.ProcessName}");

          if (foundWindow is not null)
          {
            SetForegroundWindow(foundWindow.Handle);
            SetFocus(foundWindow.Handle);
          }
        });

        Application.Run();
      }
      catch (Exception exception)
      {
        _bus.Invoke(new HandleFatalExceptionCommand(exception));
      }
    }

    private void OnApplicationExit()
    {
      _bus.Invoke(new ShowAllWindowsCommand());
      _barService.ExitApp();
      _systemTrayIcon?.Remove();
      Application.Exit();
    }
  }
}

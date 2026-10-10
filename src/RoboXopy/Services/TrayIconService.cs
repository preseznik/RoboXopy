using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using RoboXopy.Core.Models;
using RoboXopy.Infrastructure;
using RoboXopy.ViewModels;
using Forms = System.Windows.Forms;

namespace RoboXopy.Services;

/// <summary>Notification-area icon with a Fluent-styled context menu and balloon notifications.</summary>
public sealed class TrayIconService : IDisposable
{
    private readonly MainViewModel _main;
    private readonly Action _showWindow;
    private readonly Action _exit;
    private readonly Forms.NotifyIcon _icon;
    private Action? _balloonAction;
    private ContextMenu? _menu;

    public TrayIconService(MainViewModel main, Action showWindow, Action exit)
    {
        _main = main;
        _showWindow = showWindow;
        _exit = exit;
        _icon = new Forms.NotifyIcon
        {
            Icon = AppIcons.LoadTrayIcon(),
            Text = "RoboXopy",
            Visible = true,
        };
        _icon.MouseClick += OnMouseClick;
        _icon.BalloonTipClicked += (_, _) =>
        {
            var action = _balloonAction;
            _balloonAction = null;
            (action ?? _showWindow)();
        };
        _icon.BalloonTipClosed += (_, _) => _balloonAction = null;
        _main.RunStateChanged += (_, _) => UpdateToolTip();
        UpdateToolTip();
    }

    public void ShowNotification(string title, string message, Forms.ToolTipIcon icon, Action? onClick = null)
    {
        _balloonAction = onClick;
        _icon.ShowBalloonTip(5000, Truncate(title, 63), Truncate(string.IsNullOrWhiteSpace(message) ? " " : message, 255), icon);
    }

    public void UpdateToolTip()
    {
        var running = _main.Profiles.Where(p => p.IsRunning).Select(p => p.DisplayName).ToList();
        var text = running.Count switch
        {
            0 when _main.Host.Settings.SchedulerPaused => "RoboXopy – scheduled runs paused",
            0 => "RoboXopy",
            1 => $"RoboXopy – running {running[0]}",
            _ => $"RoboXopy – running {running.Count} profiles",
        };
        _icon.Text = Truncate(text, 127);
    }

    private void OnMouseClick(object? sender, Forms.MouseEventArgs e)
    {
        if (e.Button == Forms.MouseButtons.Left) _showWindow();
        else if (e.Button == Forms.MouseButtons.Right) ShowMenu();
    }

    private void ShowMenu()
    {
        if (_menu is { IsOpen: true }) _menu.IsOpen = false;

        var menu = new ContextMenu { Placement = System.Windows.Controls.Primitives.PlacementMode.MousePoint };
        var open = new MenuItem { Header = "Open RoboXopy", FontWeight = FontWeights.SemiBold };
        open.Click += (_, _) => _showWindow();
        menu.Items.Add(open);
        menu.Items.Add(new Separator());

        var run = new MenuItem { Header = "Run profile", IsEnabled = _main.Profiles.Count > 0 };
        foreach (var profile in _main.Profiles)
        {
            var item = new MenuItem
            {
                Header = profile.IsBusy ? $"{profile.DisplayName} (running)" : profile.DisplayName,
                IsEnabled = !profile.IsBusy && !profile.HasErrors,
            };
            var target = profile;
            item.Click += (_, _) => _ = target.StartAsync(dryRun: false, RunTrigger.Manual);
            run.Items.Add(item);
        }

        menu.Items.Add(run);

        if (_main.AnyRunning)
        {
            var stop = new MenuItem { Header = "Stop all runs" };
            stop.Click += (_, _) =>
            {
                foreach (var p in _main.Profiles.Where(p => p.IsRunning)) p.Session?.CancelCommand.Execute(null);
            };
            menu.Items.Add(stop);
        }

        var pause = new MenuItem { Header = "Pause scheduled runs", IsCheckable = true, IsChecked = _main.Host.Settings.SchedulerPaused };
        pause.Click += (_, _) => _main.Host.Settings.SchedulerPaused = pause.IsChecked;
        menu.Items.Add(pause);
        menu.Items.Add(new Separator());

        var exit = new MenuItem { Header = "Exit" };
        exit.Click += (_, _) => _exit();
        menu.Items.Add(exit);

        menu.Opened += (_, _) =>
        {
            if (PresentationSource.FromVisual(menu) is HwndSource source) NativeMethods.SetForegroundWindow(source.Handle);
        };
        _menu = menu;
        menu.IsOpen = true;
    }

    private static string Truncate(string text, int max) => text.Length <= max ? text : text[..(max - 1)] + "…";

    public void Dispose()
    {
        if (_menu is not null) _menu.IsOpen = false;
        _icon.Visible = false;
        _icon.Dispose();
    }
}

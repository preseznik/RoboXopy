using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using RoboXopy.Infrastructure;
using RoboXopy.ViewModels;

namespace RoboXopy.Views;

public partial class MainWindow : Window
{
    public static readonly RoutedCommand FocusSearchCommand = new(nameof(FocusSearchCommand), typeof(MainWindow));

    private readonly MainViewModel _main;

    public MainWindow(MainViewModel main)
    {
        _main = main;
        InitializeComponent();
        DataContext = main;
        RestorePlacement();

        CommandBindings.Add(new CommandBinding(FocusSearchCommand, (_, _) =>
        {
            SearchBox.Focus();
            SearchBox.SelectAll();
        }));

        main.ProfileCreated += (_, _) => Dispatcher.BeginInvoke(DispatcherPriority.Input, ProfilePage.FocusName);
        Activated += (_, _) => _main.SelectedProfile?.Validate();
        StateChanged += (_, _) => OnStateChanged();
        DpiChanged += (_, _) => UpdateMaximizedPadding();
        SourceInitialized += (_, _) => UpdateMaximizedPadding();
        PreviewKeyDown += OnPreviewKeyDown;
    }

    /// <summary>The state to return to when restoring from minimized (or from the tray).</summary>
    public WindowState RestoreState { get; private set; } = WindowState.Normal;

    public void SavePlacement()
    {
        var settings = _main.Host.Settings;
        var bounds = WindowState == WindowState.Normal ? new Rect(Left, Top, ActualWidth, ActualHeight) : RestoreBounds;
        if (bounds.IsEmpty || bounds.Width < MinWidth / 2) return;

        settings.WindowWidth = Math.Round(bounds.Width);
        settings.WindowHeight = Math.Round(bounds.Height);
        settings.WindowLeft = Math.Round(bounds.Left);
        settings.WindowTop = Math.Round(bounds.Top);
        settings.WindowMaximized = WindowState == WindowState.Maximized
            || (WindowState == WindowState.Minimized && RestoreState == WindowState.Maximized);
    }

    private void RestorePlacement()
    {
        var s = _main.Host.Settings;
        Width = Math.Max(MinWidth, s.WindowWidth > 0 ? s.WindowWidth : Width);
        Height = Math.Max(MinHeight, s.WindowHeight > 0 ? s.WindowHeight : Height);

        var screen = new Rect(SystemParameters.VirtualScreenLeft, SystemParameters.VirtualScreenTop,
            SystemParameters.VirtualScreenWidth, SystemParameters.VirtualScreenHeight);
        if (s.WindowLeft is { } left && s.WindowTop is { } top
            && screen.IntersectsWith(new Rect(left + 40, top, Math.Max(0, Width - 80), 48)))
        {
            Left = left;
            Top = top;
        }
        else
        {
            var work = SystemParameters.WorkArea;
            Width = Math.Min(Width, work.Width);
            Height = Math.Min(Height, work.Height);
            Left = work.Left + (work.Width - Width) / 2;
            Top = work.Top + (work.Height - Height) / 2;
        }

        if (s.WindowMaximized)
        {
            WindowState = WindowState.Maximized;
            RestoreState = WindowState.Maximized;
        }
    }

    private void OnStateChanged()
    {
        if (WindowState != WindowState.Minimized) RestoreState = WindowState;
        UpdateMaximizedPadding();
    }

    /// <summary>A maximized chromeless window overhangs the screen by the resize frame; pad the content so nothing is clipped.</summary>
    private void UpdateMaximizedPadding()
    {
        if (WindowState != WindowState.Maximized)
        {
            Root.Margin = new Thickness(0);
            return;
        }

        var dpi = System.Windows.Media.VisualTreeHelper.GetDpi(this);
        double frame;
        try
        {
            var raw = (uint)Math.Round(96 * dpi.DpiScaleX);
            frame = (NativeMethods.GetSystemMetricsForDpi(NativeMethods.SmCxSizeFrame, raw)
                     + NativeMethods.GetSystemMetricsForDpi(NativeMethods.SmCxPaddedBorder, raw)) / dpi.DpiScaleX;
        }
        catch (EntryPointNotFoundException)
        {
            frame = SystemParameters.WindowResizeBorderThickness.Left + 4;
        }

        Root.Margin = new Thickness(frame, frame, frame, frame);
    }

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            if (_main.Dialog.IsOpen) _main.Dialog.Close(false);
            else if (_main.Import.IsOpen) _main.Import.CancelCommand.Execute(null);
            else if (_main.IsSettingsOpen) _main.CloseSettingsCommand.Execute(null);
            else return;
            e.Handled = true;
        }
        else if (_main.IsModalOpen && IsWindowShortcut(e))
        {
            // The window's shortcuts act on what's behind the dialog or wizard.
            e.Handled = true;
        }
    }

    private static bool IsWindowShortcut(KeyEventArgs e)
    {
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        var modifiers = Keyboard.Modifiers;
        return key == Key.F5 || (modifiers == ModifierKeys.Control && key is Key.N or Key.F or Key.OemComma);
    }

    private void OnImportVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (e.NewValue is true)
        {
            Dispatcher.BeginInvoke(DispatcherPriority.Input, () => ImportPrimary.Focus());
        }
        else
        {
            // Wait for the imported profile to be added and laid out so focus lands on it.
            Dispatcher.BeginInvoke(DispatcherPriority.Background, () =>
            {
                if (ProfileList.SelectedItem is not null && ProfileList.ItemContainerGenerator.ContainerFromItem(ProfileList.SelectedItem) is UIElement item)
                {
                    item.Focus();
                }
            });
        }
    }

    private void OnDialogVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (e.NewValue is true)
        {
            Dispatcher.BeginInvoke(DispatcherPriority.Input, () => DialogPrimary.Focus());
        }
        else if (ProfileList.SelectedItem is not null && ProfileList.ItemContainerGenerator.ContainerFromItem(ProfileList.SelectedItem) is UIElement item)
        {
            item.Focus();
        }
    }
}

using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using RoboXopy.ViewModels;

namespace RoboXopy.Views;

public partial class SettingsView : UserControl
{
    public SettingsView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (e.OldValue is SettingsViewModel oldVm) oldVm.ShowUpdatesRequested -= OnShowUpdatesRequested;
        if (e.NewValue is SettingsViewModel newVm) newVm.ShowUpdatesRequested += OnShowUpdatesRequested;
    }

    private void OnShowUpdatesRequested(object? sender, EventArgs e)
    {
        // Settings may have only just been made visible; wait for layout before scrolling.
        Dispatcher.BeginInvoke(DispatcherPriority.Loaded, () =>
        {
            UpdatesSection.BringIntoView(new Rect(0, 0, UpdatesSection.ActualWidth, UpdatesSection.ActualHeight));
        });
    }
}

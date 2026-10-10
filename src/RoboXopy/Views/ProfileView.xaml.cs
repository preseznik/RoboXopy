using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using RoboXopy.ViewModels;

namespace RoboXopy.Views;

public partial class ProfileView : UserControl
{
    private const double MinOutputHeight = 120;
    private ProfileViewModel? _vm;

    public ProfileView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
        UpdateOutputRow();
    }

    /// <summary>Brings the General tab forward and puts the caret in the name box (used after creating a profile).</summary>
    public void FocusName()
    {
        Tabs.SelectedIndex = 0;
        Dispatcher.BeginInvoke(() =>
        {
            UpdateLayout();
            NameBox.Focus();
            Keyboard.Focus(NameBox);
            NameBox.SelectAll();
        }, System.Windows.Threading.DispatcherPriority.Input);
    }

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (_vm is not null) _vm.PropertyChanged -= OnViewModelPropertyChanged;
        _vm = e.NewValue as ProfileViewModel;
        if (_vm is not null) _vm.PropertyChanged += OnViewModelPropertyChanged;
        UpdateOutputRow();
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(ProfileViewModel.HasSession) or nameof(ProfileViewModel.Session))
            UpdateOutputRow();
    }

    private void UpdateOutputRow()
    {
        if (_vm?.HasSession == true)
        {
            var saved = _vm.AppSettings.OutputPanelHeight;
            OutputRow.MinHeight = MinOutputHeight;
            OutputRow.Height = new GridLength(Math.Max(140, double.IsFinite(saved) ? saved : 240));
        }
        else
        {
            OutputRow.MinHeight = 0;
            OutputRow.Height = new GridLength(0);
        }
    }

    // The thumb moves with the panel, so VerticalChange is relative to its current position; the tab area gives up space down to its MinHeight.
    private void OnSplitterDragDelta(object sender, DragDeltaEventArgs e)
    {
        var max = OutputRow.ActualHeight + Math.Max(0, ContentRow.ActualHeight - ContentRow.MinHeight);
        var height = Math.Clamp(OutputRow.ActualHeight - e.VerticalChange, MinOutputHeight, Math.Max(MinOutputHeight, max));
        OutputRow.Height = new GridLength(Math.Round(height));
    }

    private void OnSplitterDragCompleted(object sender, DragCompletedEventArgs e)
    {
        if (_vm is null || OutputRow.Height.Value < MinOutputHeight) return;
        _vm.AppSettings.OutputPanelHeight = Math.Round(OutputRow.Height.Value);
    }

    private void OnTimeBoxLostFocus(object sender, KeyboardFocusChangedEventArgs e) => _vm?.CommitTimeOfDay();

    private void OnTimeBoxKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter) return;
        TimeBox.GetBindingExpression(TextBox.TextProperty)?.UpdateSource();
        _vm?.CommitTimeOfDay();
        TimeBox.SelectAll();
        e.Handled = true;
    }
}

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

    private void OnSplitterDragCompleted(object sender, DragCompletedEventArgs e)
    {
        if (_vm is null || OutputRow.ActualHeight < MinOutputHeight) return;
        _vm.AppSettings.OutputPanelHeight = Math.Round(OutputRow.ActualHeight);
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

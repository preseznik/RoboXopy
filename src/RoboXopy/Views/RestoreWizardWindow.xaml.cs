using System.ComponentModel;
using System.Windows;
using RoboXopy.ViewModels;

namespace RoboXopy.Views;

public partial class RestoreWizardWindow : Window
{
    private readonly RestoreWizardViewModel _viewModel;

    public RestoreWizardWindow(ProfileViewModel profile, AppHost host)
    {
        InitializeComponent();
        _viewModel = new RestoreWizardViewModel(profile, host);
        _viewModel.CloseRequested += (_, _) => Close();
        DataContext = _viewModel;
        Closing += OnClosing;
    }

    private void OnClosing(object? sender, CancelEventArgs e) => e.Cancel = _viewModel.IsExecuting;
}

using System.ComponentModel;
using System.Windows;
using RoboXopy.ViewModels;

namespace RoboXopy.Views;

public partial class ProfileCreationWizardWindow : Window
{
    private readonly MainViewModel _main;
    private readonly ProfileCreationWizardViewModel _viewModel;

    public ProfileCreationWizardWindow(MainViewModel main)
    {
        _main = main;
        _viewModel = new ProfileCreationWizardViewModel(main.Host.Settings.SkipProfileWizard);
        _viewModel.ProfileCreated += OnProfileCreated;
        _viewModel.Cancelled += OnCancelled;
        InitializeComponent();
        DataContext = _viewModel;
        Closing += OnClosing;
    }

    private void OnProfileCreated(object? sender, ProfileCreatedByWizardEventArgs e)
    {
        _main.CreateProfileFromWizard(e.Profile);
        Close();
    }

    private void OnCancelled(object? sender, EventArgs e) => Close();

    private void OnClosing(object? sender, CancelEventArgs e)
    {
        _main.Host.Settings.SkipProfileWizard = _viewModel.SkipOnStartup;
    }
}

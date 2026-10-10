using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using RoboXopy.Core.Models;
using RoboXopy.Core.Scheduling;
using RoboXopy.Infrastructure;

namespace RoboXopy.ViewModels;

/// <summary>The app-wide settings page.</summary>
public sealed partial class SettingsViewModel : ObservableObject
{
    private readonly AppHost _host;

    public SettingsViewModel(AppHost host, UpdateViewModel updates)
    {
        _host = host;
        Updates = updates;
        Model.PropertyChanged += OnModelPropertyChanged;
    }

    public UpdateViewModel Updates { get; }

    /// <summary>Asks the view to bring the Updates section into view.</summary>
    public event EventHandler? ShowUpdatesRequested;

    public void RequestShowUpdates() => ShowUpdatesRequested?.Invoke(this, EventArgs.Empty);

    public AppSettings Model => _host.Settings;
    public string DataFolder => _host.Paths.Root;
    public string RobocopyResolvedPath => Model.ResolveRobocopyPath();
    public bool RobocopyFound => File.Exists(RobocopyResolvedPath);
    public string TaskFolder => TaskSchedulerService.TaskFolder;

    public string VersionText => $"Version {AppVersion.Display} · .NET {Environment.Version.ToString(2)}";

    public int[] RetentionChoices { get; } = [5, 10, 30, 60, 100, 250];

    private void OnModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(AppSettings.RobocopyPath))
        {
            OnPropertyChanged(nameof(RobocopyResolvedPath));
            OnPropertyChanged(nameof(RobocopyFound));
        }
    }

    [RelayCommand]
    private void OpenDataFolder() => Shell.Open(DataFolder);

    [RelayCommand]
    private void BrowseRobocopy()
    {
        var dialog = new OpenFileDialog
        {
            Title = "Locate robocopy.exe",
            Filter = "robocopy.exe|robocopy.exe|Programs (*.exe)|*.exe",
            CheckFileExists = true,
        };
        var current = RobocopyResolvedPath;
        if (Path.GetDirectoryName(current) is { } dir && Directory.Exists(dir)) dialog.InitialDirectory = dir;
        if (dialog.ShowDialog(System.Windows.Application.Current.MainWindow) == true) Model.RobocopyPath = dialog.FileName;
    }

    [RelayCommand]
    private void ResetRobocopyPath() => Model.RobocopyPath = "";

    [RelayCommand]
    private void OpenTaskScheduler()
    {
        try
        {
            TaskSchedulerService.OpenTaskScheduler();
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
        }
    }
}

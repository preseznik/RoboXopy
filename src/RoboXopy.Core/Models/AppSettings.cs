using CommunityToolkit.Mvvm.ComponentModel;

namespace RoboXopy.Core.Models;

public sealed partial class AppSettings : ObservableObject
{
    [ObservableProperty] private AppTheme _theme = AppTheme.System;
    [ObservableProperty] private bool _closeToTray = true;
    [ObservableProperty] private bool _startWithWindows;
    [ObservableProperty] private bool _startMinimized = true;
    [ObservableProperty] private bool _showNotifications = true;
    [ObservableProperty] private bool _notifyOnlyOnProblems;
    [ObservableProperty] private bool _schedulerPaused;
    [ObservableProperty] private bool _simultaneousScheduledRuns;
    [ObservableProperty] private int _maxSimultaneousScheduledRuns = 2;
    [ObservableProperty] private int _logRetentionCount = 30;
    [ObservableProperty] private string _robocopyPath = "";
    [ObservableProperty] private bool _checkForUpdates = true;
    [ObservableProperty] private DateTimeOffset? _lastUpdateCheck;
    /// <summary>The version that last ran with this data folder; used to announce a completed update.</summary>
    [ObservableProperty] private string _lastRunVersion = "";

    [ObservableProperty] private double _windowWidth = 1240;
    [ObservableProperty] private double _windowHeight = 820;
    [ObservableProperty] private double? _windowLeft;
    [ObservableProperty] private double? _windowTop;
    [ObservableProperty] private bool _windowMaximized;
    [ObservableProperty] private double _outputPanelHeight = 240;
    [ObservableProperty] private Guid? _lastSelectedProfileId;
    [ObservableProperty] private bool _trayHintShown;
    [ObservableProperty] private bool _skipProfileWizard;
    /// <summary>Folders that have ever been used as a source, retained after profiles are changed or deleted.</summary>
    [ObservableProperty] private IReadOnlyList<string> _sourceFolderHistory = [];

    partial void OnMaxSimultaneousScheduledRunsChanged(int value)
    {
        var clamped = Math.Clamp(value, 1, 3);
        if (value != clamped) MaxSimultaneousScheduledRuns = clamped;
    }

    public string ResolveRobocopyPath() =>
        string.IsNullOrWhiteSpace(RobocopyPath)
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "robocopy.exe")
            : Environment.ExpandEnvironmentVariables(RobocopyPath);
}

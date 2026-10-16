using System.ComponentModel;
using System.Text.Json.Serialization;
using CommunityToolkit.Mvvm.ComponentModel;

namespace RoboXopy.Core.Models;

/// <summary>A named robocopy job: source, destination, options and schedule.</summary>
public sealed partial class SyncProfile : ObservableObject
{
    [ObservableProperty] private Guid _id = Guid.NewGuid();
    [ObservableProperty] private string _name = "New profile";
    [ObservableProperty] private string _group = "";
    [ObservableProperty] private string _source = "";
    [ObservableProperty] private string _destination = "";
    /// <summary>Normalized risky destination explicitly acknowledged by the user, if it still matches <see cref="Destination"/>.</summary>
    [ObservableProperty] private string? _confirmedHistoricalSourceDestination;
    /// <summary>The highest freshness severity already notified for the current stale period.</summary>
    [ObservableProperty] private StaleSyncLevel _lastStaleNotificationLevel;
    /// <summary>Scheduled preview that last produced a user notification, preventing duplicates after history refreshes.</summary>
    [ObservableProperty] private Guid? _lastScheduledPreviewNotificationId;
    [ObservableProperty] private int _sortOrder;
    [ObservableProperty] private RobocopyOptions _options = new();
    [ObservableProperty] private ScheduleSettings _schedule = new();

    /// <summary>Full Task Scheduler path of the task registered for this profile, if any.</summary>
    [ObservableProperty] private string? _registeredTaskName;

    /// <summary>Executable path that was used when the task was registered (re-registered when the app moves).</summary>
    [ObservableProperty] private string? _registeredTaskExePath;

    /// <summary>Schedule fingerprint at the time the task was registered.</summary>
    [ObservableProperty] private string? _registeredTaskFingerprint;

    [ObservableProperty] private DateTimeOffset _createdAt = DateTimeOffset.Now;
    [ObservableProperty] private DateTimeOffset _modifiedAt = DateTimeOffset.Now;

    /// <summary>Raised when the profile or any nested option/schedule value changes.</summary>
    public event EventHandler<string>? Changed;

    public SyncProfile()
    {
        _options.PropertyChanged += OnOptionsPropertyChanged;
        _schedule.PropertyChanged += OnSchedulePropertyChanged;
    }

    [JsonIgnore]
    public string ShortId => Id.ToString("N")[..8];

    partial void OnOptionsChanged(RobocopyOptions? oldValue, RobocopyOptions newValue)
    {
        if (oldValue is not null) oldValue.PropertyChanged -= OnOptionsPropertyChanged;
        newValue.PropertyChanged += OnOptionsPropertyChanged;
    }

    partial void OnScheduleChanged(ScheduleSettings? oldValue, ScheduleSettings newValue)
    {
        if (oldValue is not null) oldValue.PropertyChanged -= OnSchedulePropertyChanged;
        newValue.PropertyChanged += OnSchedulePropertyChanged;
    }

    private void OnOptionsPropertyChanged(object? sender, PropertyChangedEventArgs e)
        => Changed?.Invoke(this, $"{nameof(Options)}.{e.PropertyName}");

    private void OnSchedulePropertyChanged(object? sender, PropertyChangedEventArgs e)
        => Changed?.Invoke(this, $"{nameof(Schedule)}.{e.PropertyName}");

    protected override void OnPropertyChanged(PropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);
        if (e.PropertyName is not (nameof(ModifiedAt) or nameof(ShortId)))
        {
            Changed?.Invoke(this, e.PropertyName ?? "");
        }
    }

    /// <summary>Deep copy with a fresh id and no Task Scheduler registration.</summary>
    public SyncProfile Duplicate(string name)
    {
        var json = System.Text.Json.JsonSerializer.Serialize(this, Storage.JsonDefaults.Options);
        var copy = System.Text.Json.JsonSerializer.Deserialize<SyncProfile>(json, Storage.JsonDefaults.Options)!;
        copy.Id = Guid.NewGuid();
        copy.Name = name;
        copy.RegisteredTaskName = null;
        copy.RegisteredTaskExePath = null;
        copy.RegisteredTaskFingerprint = null;
        copy.CreatedAt = DateTimeOffset.Now;
        copy.ModifiedAt = DateTimeOffset.Now;
        return copy;
    }
}

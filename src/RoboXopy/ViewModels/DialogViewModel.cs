using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace RoboXopy.ViewModels;

/// <summary>Backs the in-window content dialog overlay.</summary>
public sealed partial class DialogViewModel : ObservableObject
{
    private TaskCompletionSource<bool>? _completion;

    [ObservableProperty] private bool _isOpen;
    [ObservableProperty] private string _title = "";
    [ObservableProperty] private string _message = "";
    [ObservableProperty] private string _primaryText = "OK";
    [ObservableProperty] private string? _secondaryText;
    [ObservableProperty] private bool _isDestructive;

    public Task<bool> ShowAsync(string title, string message, string primaryText, string? secondaryText = "Cancel", bool destructive = false)
    {
        _completion?.TrySetResult(false);
        _completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        Title = title;
        Message = message;
        PrimaryText = primaryText;
        SecondaryText = secondaryText;
        IsDestructive = destructive;
        IsOpen = true;
        return _completion.Task;
    }

    [RelayCommand]
    private void Primary() => Close(true);

    [RelayCommand]
    private void Secondary() => Close(false);

    public void Close(bool result)
    {
        if (!IsOpen) return;
        IsOpen = false;
        var completion = _completion;
        _completion = null;
        completion?.TrySetResult(result);
    }
}

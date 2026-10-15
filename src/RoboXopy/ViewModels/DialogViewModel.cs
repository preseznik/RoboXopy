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
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(RequiresTextConfirmation))]
    [NotifyCanExecuteChangedFor(nameof(PrimaryCommand))]
    private string? _requiredConfirmationText;
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(PrimaryCommand))]
    private string _confirmationText = "";

    public bool RequiresTextConfirmation => !string.IsNullOrEmpty(RequiredConfirmationText);

    public Task<bool> ShowAsync(string title, string message, string primaryText, string? secondaryText = "Cancel", bool destructive = false)
    {
        _completion?.TrySetResult(false);
        _completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        Title = title;
        Message = message;
        PrimaryText = primaryText;
        SecondaryText = secondaryText;
        IsDestructive = destructive;
        RequiredConfirmationText = null;
        ConfirmationText = "";
        IsOpen = true;
        return _completion.Task;
    }

    public Task<bool> ShowTypedConfirmationAsync(string title, string message, string primaryText, string requiredText, bool destructive = true)
    {
        _completion?.TrySetResult(false);
        _completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        Title = title;
        Message = message;
        PrimaryText = primaryText;
        SecondaryText = "Cancel";
        IsDestructive = destructive;
        RequiredConfirmationText = requiredText;
        ConfirmationText = "";
        IsOpen = true;
        return _completion.Task;
    }

    [RelayCommand(CanExecute = nameof(CanConfirm))]
    private void Primary() => Close(true);

    private bool CanConfirm() => !RequiresTextConfirmation
        || string.Equals(ConfirmationText.Trim(), RequiredConfirmationText, StringComparison.Ordinal);

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

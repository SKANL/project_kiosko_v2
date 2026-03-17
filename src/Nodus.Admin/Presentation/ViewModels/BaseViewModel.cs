using CommunityToolkit.Mvvm.ComponentModel;

namespace Nodus.Admin.Presentation.ViewModels;

/// <summary>
/// Base ViewModel for all Admin pages. Provides common IsBusy tracking and safe
/// async-command execution with automatic error surfacing.
/// </summary>
public abstract partial class BaseViewModel : ObservableObject
{
    private bool _isBusy;
    public bool IsBusy
    {
        get => _isBusy;
        set
        {
            if (SetProperty(ref _isBusy, value))
                OnPropertyChanged(nameof(IsNotBusy));
        }
    }

    private string _title = string.Empty;
    public string Title
    {
        get => _title;
        set => SetProperty(ref _title, value);
    }

    private string _errorMessage = string.Empty;
    public string ErrorMessage
    {
        get => _errorMessage;
        set => SetProperty(ref _errorMessage, value);
    }

    private bool _hasError;
    public bool HasError
    {
        get => _hasError;
        set => SetProperty(ref _hasError, value);
    }

    public bool IsNotBusy => !IsBusy;

    /// <summary>
    /// Wraps an async task: sets IsBusy, clears prior errors, then surfaces any
    /// exception as a user-visible ErrorMessage instead of crashing.
    /// </summary>
    protected async Task SafeExecuteAsync(Func<Task> action)
    {
        if (IsBusy) return;
        try
        {
            IsBusy       = true;
            HasError     = false;
            ErrorMessage = string.Empty;
            await action();
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
            HasError     = true;
        }
        finally
        {
            IsBusy = false;
        }
    }
}

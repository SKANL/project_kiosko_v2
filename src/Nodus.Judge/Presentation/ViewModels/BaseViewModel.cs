using CommunityToolkit.Mvvm.ComponentModel;

namespace Nodus.Judge.Presentation.ViewModels;

/// <summary>
/// Base ViewModel for all Judge pages. Provides IsBusy + safe async execution.
/// </summary>
public abstract partial class BaseViewModel : ObservableObject
{
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsNotBusy))]
    private bool _isBusy;

    [ObservableProperty]
    private string _title = string.Empty;

    [ObservableProperty]
    private string _errorMessage = string.Empty;

    [ObservableProperty]
    private bool _hasError;

    public bool IsNotBusy => !IsBusy;

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

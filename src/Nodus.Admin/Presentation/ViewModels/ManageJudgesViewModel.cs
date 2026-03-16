using System.Collections.ObjectModel;
using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Nodus.Admin.Application.Interfaces.Persistence;
using Nodus.Admin.Application.Interfaces.Services;
using Nodus.Admin.Domain.Entities;

namespace Nodus.Admin.Presentation.ViewModels;

public sealed class ManageJudgeItem : ObservableObject
{
    private Judge _judge;
    public required Judge Judge 
    { 
        get => _judge; 
        set => SetProperty(ref _judge, value); 
    }
    
    public required ICommand ToggleBlockCommand { get; init; }

    public string BlockStatusLabel => Judge.IsBlocked ? "Bloqueado" : "Permitido";
    public string BlockStatusColor => Judge.IsBlocked ? "#FF3B30" : "#34C759";
    public string BlockActionText => Judge.IsBlocked ? "Desbloquear" : "Bloquear";

    public void RefreshProperties()
    {
        OnPropertyChanged(nameof(Judge));
        OnPropertyChanged(nameof(BlockStatusLabel));
        OnPropertyChanged(nameof(BlockStatusColor));
        OnPropertyChanged(nameof(BlockActionText));
    }
}

public sealed partial class ManageJudgesViewModel : BaseViewModel
{
    private readonly IJudgeRepository _judges;
    private readonly IAppSettingsService _settings;

    public ObservableCollection<ManageJudgeItem> Judges { get; } = new();

    [ObservableProperty] private bool _hasJudges;

    public ManageJudgesViewModel(IJudgeRepository judges, IAppSettingsService settings)
    {
        _judges = judges;
        _settings = settings;
        Title = "Jueces";
    }

    [RelayCommand]
    public async Task AppearingAsync() => await LoadJudgesAsync();

    private async Task LoadJudgesAsync()
    {
        var eventId = _settings.ActiveEventId;
        if (!eventId.HasValue)
        {
            HasJudges = false;
            return;
        }

        var result = await _judges.GetByEventAsync(eventId.Value);
        if (result.IsFail)
        {
            ErrorMessage = result.Error!;
            HasError = true;
            return;
        }

        Judges.Clear();
        foreach (var judge in result.Value!)
        {
            var item = new ManageJudgeItem
            {
                Judge = judge,
                ToggleBlockCommand = new AsyncRelayCommand(async () => await ToggleBlockJudgeAsync(judge))
            };
            Judges.Add(item);
        }

        HasJudges = Judges.Count > 0;
    }

    private async Task ToggleBlockJudgeAsync(Judge judge)
    {
        var actionText = judge.IsBlocked ? "desbloquear" : "bloquear";
        bool confirm = await Shell.Current.DisplayAlertAsync(
            "Confirmar acción",
            $"¿Estás seguro que deseas {actionText} a {judge.Name}?",
            "Sí",
            "Cancelar");

        if (!confirm) return;

        judge.IsBlocked = !judge.IsBlocked;
        var result = await _judges.UpdateAsync(judge);
        if (result.IsFail)
        {
            ErrorMessage = result.Error!;
            HasError = true;
            judge.IsBlocked = !judge.IsBlocked; // revert on fail
            return;
        }

        // Trigger UI update
        var item = Judges.FirstOrDefault(i => i.Judge.Id == judge.Id);
        item?.RefreshProperties();
    }
}

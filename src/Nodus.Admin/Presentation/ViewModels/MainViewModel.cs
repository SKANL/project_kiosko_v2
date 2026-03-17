using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Nodus.Admin.Application.Interfaces.Persistence;
using Nodus.Admin.Application.Interfaces.Services;
using Nodus.Admin.Application.UseCases.Votes;
using Nodus.Admin.Infrastructure.Persistence;

namespace Nodus.Admin.Presentation.ViewModels;

/// <summary>
/// Dashboard — shows live BLE status, current event, total votes received.
/// Start / stop the GATT server from here.
/// </summary>
public sealed partial class MainViewModel : BaseViewModel
{
    private readonly IBleGattServerService _ble;
    private readonly IEventRepository      _events;
    private readonly IVoteRepository       _votes;
    private readonly IAppSettingsService   _settings;
    private readonly NodusDatabase         _database;

    public MainViewModel(
        IBleGattServerService ble,
        IEventRepository events,
        IVoteRepository votes,
        IAppSettingsService settings,
        NodusDatabase database)
    {
        _ble      = ble;
        _events   = events;
        _votes    = votes;
        _settings = settings;
        _database = database;
        Title     = "Nodus Admin";
    }

    // ── Observable state ──────────────────────────────────────────────

    private bool _isBleRunning;
    public bool IsBleRunning
    {
        get => _isBleRunning;
        set => SetProperty(ref _isBleRunning, value);
    }

    private string _eventName = "Sin evento activo";
    public string EventName
    {
        get => _eventName;
        set => SetProperty(ref _eventName, value);
    }

    private string _eventStatus = "—";
    public string EventStatus
    {
        get => _eventStatus;
        set => SetProperty(ref _eventStatus, value);
    }

    private int _totalVotes;
    public int TotalVotes
    {
        get => _totalVotes;
        set => SetProperty(ref _totalVotes, value);
    }

    private string _deviceId = string.Empty;
    public string DeviceId
    {
        get => _deviceId;
        set => SetProperty(ref _deviceId, value);
    }

    // ── Lifecycle ─────────────────────────────────────────────────────

    [RelayCommand]
    public async Task AppearingAsync()
    {
        DeviceId     = Environment.MachineName;
        IsBleRunning = _ble.IsRunning;

        var activeId = _settings.ActiveEventId;

        if (activeId.HasValue && activeId.Value > 0)
        {
            var ev = await _events.GetByIdAsync(activeId.Value);
            if (ev.IsOk)
            {
                EventName   = ev.Value!.Name;
                EventStatus = ev.Value.Status switch
                {
                    Domain.Enums.EventStatus.Active   => "Activo",
                    Domain.Enums.EventStatus.Paused   => "Pausado",
                    Domain.Enums.EventStatus.Finished => "Finalizado",
                    _                                 => "Borrador"
                };
            }

            var votes = await _votes.GetByEventAsync(activeId.Value);
            TotalVotes = votes.IsOk ? votes.Value!.Count : 0;
        }
        else
        {
            EventName   = "Sin evento activo";
            EventStatus = "—";
            TotalVotes  = 0;
        }
    }

    // ── Commands ─────────────────────────────────────────────────────

    [RelayCommand]
    private async Task ToggleBleAsync()
        => await SafeExecuteAsync(async () =>
        {
            if (_ble.IsRunning)
            {
                var stop = await _ble.StopAsync();
                if (stop.IsFail) { ErrorMessage = stop.Error!; HasError = true; return; }
                IsBleRunning = false;
            }
            else
            {
                var start = await _ble.StartAsync();
                if (start.IsFail) { ErrorMessage = start.Error!; HasError = true; return; }
                IsBleRunning = true;
            }
        });

    [RelayCommand]
    private async Task RefreshAsync()
        => await AppearingAsync();

    [RelayCommand]
    private async Task ResetAppAsync()
    {
        bool confirm = await Microsoft.Maui.Controls.Shell.Current.DisplayAlertAsync(
            "Restablecer Aplicación",
            "¿Estás seguro de que deseas borrar TODOS los datos? Esta acción eliminará eventos, proyectos, jueces y votos permanentemente.",
            "Borrar Todo",
            "Cancelar");

        if (!confirm) return;

        await SafeExecuteAsync(async () =>
        {
            // Stop BLE if running
            if (_ble.IsRunning) await _ble.StopAsync();

            // Purge DB and Settings
            await _database.PurgeAllDataAsync();
            _settings.Reset();

            // Refresh UI state
            await AppearingAsync();

            await Microsoft.Maui.Controls.Shell.Current.DisplayAlertAsync("Éxito", "La aplicación ha sido restablecida.", "OK");
        });
    }
}

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Nodus.Admin.Application.Interfaces.Persistence;
using Nodus.Admin.Application.Interfaces.Services;

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

    public MainViewModel(
        IBleGattServerService ble,
        IEventRepository events,
        IVoteRepository votes,
        IAppSettingsService settings)
    {
        _ble      = ble;
        _events   = events;
        _votes    = votes;
        _settings = settings;
        Title     = "Nodus Admin";
    }

    // ── Observable state ──────────────────────────────────────────────

    [ObservableProperty] private bool   _isBleRunning;
    [ObservableProperty] private string _eventName      = "No active event";
    [ObservableProperty] private string _eventStatus    = "—";
    [ObservableProperty] private int    _totalVotes;
    [ObservableProperty] private string _deviceId       = string.Empty;

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
                EventStatus = ev.Value.Status.ToString();
            }

            var votes = await _votes.GetByEventAsync(activeId.Value);
            TotalVotes = votes.IsOk ? votes.Value!.Count : 0;
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
}

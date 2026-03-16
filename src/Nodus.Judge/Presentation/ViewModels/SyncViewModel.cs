using System.Collections.ObjectModel;
using System.Reactive.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Nodus.Judge.Application.Interfaces.Services;
using Nodus.Judge.Domain.Enums;

namespace Nodus.Judge.Presentation.ViewModels;

/// <summary>
/// Shows the Firefly swarm state and discovered peers.
/// Also exposes start/stop controls for the swarm protocol.
/// </summary>
public sealed partial class SyncViewModel : BaseViewModel, IDisposable
{
    private readonly IBleSwarmService _swarm;
    private IDisposable? _stateSub;
    private IDisposable? _peerSub;

    public SyncViewModel(IBleSwarmService swarm)
    {
        _swarm = swarm;
        Title  = "Sync Status";
    }

    // ── State ─────────────────────────────────────────────────────────

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StateDisplayName))]
    [NotifyPropertyChangedFor(nameof(StateColor))]
    private FireflyState _currentState = FireflyState.Seeker;

    public string StateDisplayName => CurrentState switch
    {
        FireflyState.Seeker    => "Buscando una ruta cercana",
        FireflyState.Candidate => "Comprobando la mejor conexión",
        FireflyState.Link      => "Ayudando a mover votos",
        FireflyState.Cooldown  => "Descansando para ahorrar batería",
        _                      => "Unknown"
    };

    /// <summary>Returns a color name suitable for use in XAML StaticResource lookups.</summary>
    public string StateColor => CurrentState switch
    {
        FireflyState.Seeker    => "NodusInfo",
        FireflyState.Candidate => "NodusWarning",
        FireflyState.Link      => "NodusSuccess",
        FireflyState.Cooldown  => "NodusSecondaryLabel",
        _                      => "NodusSecondaryLabel"
    };

    [ObservableProperty] private string? _linkedPeerId;
    [ObservableProperty] private bool    _isRunning;
    [ObservableProperty] private string  _lastPeerSeenAt = "—";

    public ObservableCollection<string> DiscoveredPeers { get; } = new();

    // ── Lifecycle ─────────────────────────────────────────────────────

    [RelayCommand]
    public void Appearing()
    {
        IsRunning    = false; // swarm doesn't expose IsRunning; reflect state instead
        CurrentState = _swarm.CurrentState;
        LinkedPeerId = _swarm.LinkedPeerId;

        _stateSub = _swarm.StateChanges
            .ObserveOn(SynchronizationContext.Current!)
            .Subscribe(state =>
            {
                CurrentState = state;
                LinkedPeerId = _swarm.LinkedPeerId;
            });

        _peerSub = _swarm.PeerDiscovered
            .ObserveOn(SynchronizationContext.Current!)
            .Subscribe(peerId =>
            {
                if (!DiscoveredPeers.Contains(peerId))
                    DiscoveredPeers.Insert(0, peerId);
                LastPeerSeenAt = DateTime.Now.ToString("HH:mm:ss");
            });
    }

    [RelayCommand]
    public void Disappearing()
    {
        _stateSub?.Dispose();
        _peerSub?.Dispose();
        _stateSub = null;
        _peerSub  = null;
    }

    // ── Commands ─────────────────────────────────────────────────────

    [RelayCommand]
    private async Task ToggleSwarmAsync()
        => await SafeExecuteAsync(async () =>
        {
            if (CurrentState != FireflyState.Seeker)
            {
                var stop = await _swarm.StopAsync();
                if (stop.IsFail) { ErrorMessage = stop.Error!; HasError = true; }
                IsRunning = false;
            }
            else
            {
                var start = await _swarm.StartAsync();
                if (start.IsFail) { ErrorMessage = start.Error!; HasError = true; }
                IsRunning = start.IsOk;
            }
        });

    [RelayCommand]
    private void ClearPeers() => DiscoveredPeers.Clear();

    public void Dispose()
    {
        _stateSub?.Dispose();
        _peerSub?.Dispose();
    }
}

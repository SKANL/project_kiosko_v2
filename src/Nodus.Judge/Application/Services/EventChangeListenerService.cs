using Nodus.Judge.Application.Interfaces.Services;
using Nodus.Judge.Application.UseCases.Onboarding;

namespace Nodus.Judge.Application.Services;

/// <summary>
/// Singleton service that listens for BLE 0x07 (EventChanged) notifications
/// from the Admin GATT server and informs the user to re-synchronise.
/// </summary>
public sealed class EventChangeListenerService
{
    private readonly IBleGattClientService _client;
    private readonly SyncFromAdminUseCase _sync;
    private IDisposable? _subscription;

    public EventChangeListenerService(IBleGattClientService client, SyncFromAdminUseCase sync)
    {
        _client = client;
        _sync = sync;
    }

    public void Start()
    {
        _subscription = _client.EventChangeReceived.Subscribe(_ =>
            MainThread.BeginInvokeOnMainThread(async () =>
            {
                try
                {
                    var result = await _sync.ExecuteAsync();
                    var shell = Shell.Current;
                    if (shell?.CurrentPage is not null)
                    {
                        var message = result.IsOk
                            ? "La información del evento se actualizó automáticamente."
                            : "El evento cambió. La app reintentará actualizar los datos en segundo plano.";

                        await shell.DisplayAlertAsync("Información actualizada", message, "Continuar");
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine(
                        $"EventChangeListenerService: {ex.GetType().Name}: {ex.Message}");
                }
            }));
    }

    public void Stop()
    {
        _subscription?.Dispose();
        _subscription = null;
    }
}

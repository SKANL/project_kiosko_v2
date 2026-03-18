namespace Nodus.Admin.Application.Interfaces.Services;

/// <summary>
/// Background service that handles synchronization between the local Admin app
/// and the Cloud API (Back4App).
/// </summary>
public interface ICloudSyncService
{
    /// <summary>
    /// Starts the background sync loop (periodic pull of registrations).
    /// </summary>
    Task StartAsync();

    /// <summary>
    /// Stops the background sync loop.
    /// </summary>
    void Stop();

    /// <summary>
    /// Forces a push of the current active event data to the Cloud API.
    /// </summary>
    Task<bool> PushActiveEventAsync(int eventId);
}

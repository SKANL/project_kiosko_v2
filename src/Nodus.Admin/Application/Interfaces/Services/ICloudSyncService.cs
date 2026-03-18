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
    /// The Cloud API base URL currently configured (read from AppSettings).
    /// </summary>
    string CloudApiUrl { get; }

    /// <summary>
    /// Forces a push of the current active event data to the Cloud API.
    /// Returns (success, errorMessage)
    /// </summary>
    Task<(bool Success, string? Error)> PushActiveEventAsync(int eventId);
}

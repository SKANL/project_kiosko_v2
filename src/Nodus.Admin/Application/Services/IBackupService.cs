namespace Nodus.Admin.Application.Services;

/// <summary>
/// Provides periodic automatic backup of the SQLite database.
/// Keeps a rolling window of the last 3 backup files.
/// </summary>
public interface IBackupService
{
    /// <summary>Start the background 5-minute backup timer.</summary>
    void Start();

    /// <summary>Force an immediate backup regardless of timer state.</summary>
    Task RunBackupAsync();
}

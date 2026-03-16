using Nodus.Admin.Application.Interfaces.Services;
using Nodus.Admin.Application.Services;

namespace Nodus.Admin.Infrastructure.Backup;

/// <summary>
/// Copies the SQLite database file at a configurable interval.
/// Retains only the last <see cref="MaxBackupCount"/> files (rolling rotation).
/// </summary>
public sealed class BackupService : IBackupService, IDisposable
{
    private const int MaxBackupCount    = 3;
    private const int IntervalMinutes   = 5;

    private readonly IAppSettingsService _settings;
    private Timer?   _timer;

    public BackupService(IAppSettingsService settings)
    {
        _settings = settings;
    }

    public void Start()
    {
        var interval = TimeSpan.FromMinutes(IntervalMinutes);
        // First tick after one interval; subsequent ticks every interval.
        _timer = new Timer(_ => RunBackupAsync().GetAwaiter().GetResult(),
                           null, interval, interval);
    }

    public async Task RunBackupAsync()
    {
        try
        {
            string source = _settings.DatabasePath;
            if (!File.Exists(source)) return;

            string backupDir = Path.Combine(
                Path.GetDirectoryName(source)!,
                "backups");

            Directory.CreateDirectory(backupDir);

            string timestamp = DateTime.UtcNow.ToString("yyyyMMdd_HHmmss");
            string dest      = Path.Combine(backupDir, $"nodus_admin_{timestamp}.db3");

            // SQLite WAL checkpoint: copy via file-copy (safe for SQLite because we
            // have the connection open with SharedCache and no exclusive lock).
            await Task.Run(() => File.Copy(source, dest, overwrite: true));

            // Rotate: delete files beyond MaxBackupCount (oldest first).
            var backups = Directory.GetFiles(backupDir, "nodus_admin_*.db3")
                                   .OrderBy(f => f)
                                   .ToList();

            foreach (var old in backups.Take(Math.Max(0, backups.Count - MaxBackupCount)))
                File.Delete(old);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[BackupService] Backup failed: {ex.Message}");
        }
    }

    public void Dispose() => _timer?.Dispose();
}

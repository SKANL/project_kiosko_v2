namespace Nodus.Judge.Domain.Enums;

public enum SyncStatus
{
    Pending,
    Synced,
    Failed,
    /// <summary>Score confirmed received at Admin node via BLE.</summary>
    ReachedAdmin,
    /// <summary>Admin synced to MongoDB via Nodus.API.</summary>
    ReachedCloud
}

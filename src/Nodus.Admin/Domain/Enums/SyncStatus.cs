namespace Nodus.Admin.Domain.Enums;

public enum SyncStatus
{
    Pending,
    Synced,
    Failed,
    /// <summary>Score/media confirmed received at Admin node via BLE.</summary>
    ReachedAdmin,
    /// <summary>Admin has synced this item to MongoDB via Nodus.API.</summary>
    ReachedCloud
}

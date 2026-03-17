namespace Nodus.Admin.Application.Interfaces.Services;

/// <summary>
/// Persists lightweight app settings (active event ID, window state, etc.).
/// Backed by a simple JSON file — not the SQLite DB.
/// </summary>
public interface IAppSettingsService
{
    int?   ActiveEventId   { get; set; }
    bool   IsFirstRun      { get; }
    string DatabasePath    { get; }

    void Save();
    void Load();
    void Reset();
}

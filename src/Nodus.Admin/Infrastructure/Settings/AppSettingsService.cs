using System.Text.Json;
using Nodus.Admin.Application.Interfaces.Services;

namespace Nodus.Admin.Infrastructure.Settings;

public sealed class AppSettingsService : IAppSettingsService
{
    private const string FileName = "nodus_admin_settings.json";
    private readonly string _filePath;
    private AppSettingsData _data = new();

    public AppSettingsService()
    {
        _filePath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Nodus", FileName);
        Directory.CreateDirectory(Path.GetDirectoryName(_filePath)!);
        Load();
    }

    public int?   ActiveEventId { get => _data.ActiveEventId; set => _data.ActiveEventId = value; }
    public bool   IsFirstRun    => !File.Exists(_filePath) || _data.ActiveEventId is null;
    public string DatabasePath  => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Nodus", "nodus_admin.db3");

    // Back4App URL — set once here, used by CloudSyncService and EventQrViewModel
    public string CloudApiUrl
    {
        get => string.IsNullOrEmpty(_data.CloudApiUrl)
            ? "https://nodusapi-kk2jf5eg.b4a.run"
            : _data.CloudApiUrl;
        set => _data.CloudApiUrl = value;
    }

    public void Save()
    {
        string json = JsonSerializer.Serialize(_data, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(_filePath, json);
    }

    public void Load()
    {
        if (!File.Exists(_filePath)) return;
        try
        {
            string json = File.ReadAllText(_filePath);
            _data = JsonSerializer.Deserialize<AppSettingsData>(json) ?? new();
        }
        catch { _data = new(); }
    }

    public void Reset()
    {
        _data = new();
        if (File.Exists(_filePath)) File.Delete(_filePath);
        Save();
    }

    private sealed class AppSettingsData
    {
        public int?   ActiveEventId { get; set; }
        public string? CloudApiUrl  { get; set; }  // null = use default
    }
}

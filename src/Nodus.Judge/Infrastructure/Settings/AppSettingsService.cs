using System.Text.Json;
using Nodus.Judge.Application.Interfaces.Services;

namespace Nodus.Judge.Infrastructure.Settings;

public sealed class AppSettingsService : IAppSettingsService
{
    private const string FileName = "nodus_judge_settings.json";
    private readonly string _filePath;
    private AppSettingsData _data = new();
    private string _sessionPin = string.Empty;  // In-memory PIN for current session

    public AppSettingsService()
    {
        _filePath = Path.Combine(FileSystem.AppDataDirectory, FileName);
        Load();
    }

    public int?   ActiveEventId    { get => _data.ActiveEventId;  set => _data.ActiveEventId = value; }
    public int?   SelfJudgeId       { get => _data.SelfJudgeId;    set => _data.SelfJudgeId   = value; }
    public string JudgeName         { get => _data.JudgeName ?? string.Empty; set => _data.JudgeName = value; }
    public string PinHash           { get => _data.PinHash ?? string.Empty; set => _data.PinHash = value; }
    public string SharedEventKey    { get => _data.SharedEventKey ?? string.Empty; set => _data.SharedEventKey = value; }
    public string SessionPin        { get => _sessionPin; set => _sessionPin = value ?? string.Empty; }
    public int    BleTimeoutSeconds { get => _data.BleTimeoutSeconds > 0 ? _data.BleTimeoutSeconds : 15; set => _data.BleTimeoutSeconds = value; }
    public string PinLastChangedAt  { get => _data.PinLastChangedAt ?? string.Empty; set => _data.PinLastChangedAt = value; }
    public bool   IsOnboarded       => _data.ActiveEventId.HasValue && _data.SelfJudgeId.HasValue && !string.IsNullOrEmpty(_data.PinHash);
    public string DatabasePath      => Path.Combine(FileSystem.AppDataDirectory, "nodus_judge.db3");

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

    private sealed class AppSettingsData
    {
        public int?   ActiveEventId    { get; set; }
        public int?   SelfJudgeId       { get; set; }
        public string? JudgeName        { get; set; }
        public string? PinHash          { get; set; }
        public string? SharedEventKey   { get; set; }
        public int    BleTimeoutSeconds { get; set; } = 15;
        public string? PinLastChangedAt { get; set; }
    }
}

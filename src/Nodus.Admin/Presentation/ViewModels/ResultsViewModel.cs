using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Nodus.Admin.Application.DTOs;
using Nodus.Admin.Application.Interfaces.Services;
using Nodus.Admin.Application.UseCases.Votes;

namespace Nodus.Admin.Presentation.ViewModels;

/// <summary>
/// Displays ranked project scores for the active event and allows exporting
/// to Excel (.xlsx) via ClosedXML.
/// </summary>
public sealed partial class ResultsViewModel : BaseViewModel
{
    private readonly GetVoteSummaryUseCase _summary;
    private readonly IExcelExportService  _excel;
    private readonly IAppSettingsService  _settings;

    public ResultsViewModel(
        GetVoteSummaryUseCase summary,
        IExcelExportService excel,
        IAppSettingsService settings)
    {
        _summary  = summary;
        _excel    = excel;
        _settings = settings;
        Title     = "Resultados";
    }

    // ── State ─────────────────────────────────────────────────────────
    /// <summary>When set via QueryProperty, overrides ActiveEventId for this page visit.</summary>
    private int? _viewEventId;
    private string _eventName = "—";
    public string EventName
    {
        get => _eventName;
        set => SetProperty(ref _eventName, value);
    }

    private bool _hasResults;
    public bool HasResults
    {
        get => _hasResults;
        set => SetProperty(ref _hasResults, value);
    }

    private string _exportedFilePath = string.Empty;
    public string ExportedFilePath
    {
        get => _exportedFilePath;
        set => SetProperty(ref _exportedFilePath, value);
    }

    private bool _exportSucceeded;
    public bool ExportSucceeded
    {
        get => _exportSucceeded;
        set => SetProperty(ref _exportSucceeded, value);
    }

    public ObservableCollection<ProjectScoreDto> Rankings { get; } = new();

    // ── Lifecycle ─────────────────────────────────────────────────────

    /// <summary>Called by ResultsPage QueryProperty setter to view a specific event's results.</summary>
    public void SetViewEventId(int id) => _viewEventId = id > 0 ? id : null;

    [RelayCommand]
    public async Task AppearingAsync()
        => await RefreshAsync();

    // ── Commands ─────────────────────────────────────────────────────

    [RelayCommand]
    public async Task RefreshAsync()
        => await SafeExecuteAsync(async () =>
        {
            var eventId = _viewEventId ?? _settings.ActiveEventId;
            if (!eventId.HasValue || eventId.Value <= 0)
            {
                HasResults = false;
                return;
            }

            var result = await _summary.ExecuteAsync(eventId.Value);
            if (result.IsFail)
            {
                ErrorMessage = result.Error!;
                HasError     = true;
                return;
            }

            var dto = result.Value!;
            EventName = dto.EventName;

            Rankings.Clear();
            foreach (var r in dto.Rankings)
                Rankings.Add(r);

            HasResults = Rankings.Count > 0;
        });

    [RelayCommand]
    private async Task ExportToExcelAsync()
        => await SafeExecuteAsync(async () =>
        {
            ExportSucceeded = false;
            var eventId = _viewEventId ?? _settings.ActiveEventId;
            if (!eventId.HasValue || eventId.Value <= 0) return;

            var result = await _summary.ExecuteAsync(eventId.Value);
            if (result.IsFail) return;

            var outputDir = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
            var exportResult = await _excel.ExportAsync(eventId.Value, outputDir);
            if (exportResult.IsFail) return;
            var filePath = exportResult.Value!;
            ExportedFilePath = filePath;
            ExportSucceeded  = true;

            // Launch the file in the default spreadsheet application
            if (File.Exists(filePath))
                await Launcher.TryOpenAsync(new Uri($"file:///{filePath.Replace('\\', '/')}"));
        });
}

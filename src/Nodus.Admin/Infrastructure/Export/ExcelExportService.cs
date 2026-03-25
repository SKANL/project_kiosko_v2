using ClosedXML.Excel;
using Nodus.Admin.Application.DTOs;
using Nodus.Admin.Application.Interfaces.Persistence;
using Nodus.Admin.Application.Interfaces.Services;
using Nodus.Admin.Application.UseCases.Votes;
using Nodus.Admin.Domain.Common;
using Nodus.Admin.Domain.Entities;

namespace Nodus.Admin.Infrastructure.Export;

/// <summary>
/// Exports event results to Results_Final.xlsx (Decision #55).
/// Sheet 1: Rankings   — rank, project, mean score, vote count
/// Sheet 2: Detail     — one row per judge/project vote
/// </summary>
public sealed class ExcelExportService : IExcelExportService
{
    private readonly GetVoteSummaryUseCase _summary;
    private readonly IVoteRepository       _votes;
    private readonly IJudgeRepository      _judges;
    private readonly IProjectRepository    _projects;

    public ExcelExportService(
        GetVoteSummaryUseCase summary,
        IVoteRepository       votes,
        IJudgeRepository      judges,
        IProjectRepository    projects)
    {
        _summary  = summary;
        _votes    = votes;
        _judges   = judges;
        _projects = projects;
    }

    public async Task<Result<string>> ExportAsync(int eventId, string outputDirectory)
    {
        var summaryResult = await _summary.ExecuteAsync(eventId);
        if (summaryResult.IsFail) return Result<string>.Fail(summaryResult.Error!);

        var votesResult  = await _votes.GetByEventAsync(eventId);
        var judgesResult = await _judges.GetByEventAsync(eventId);
        var projResult   = await _projects.GetByEventAsync(eventId);

        try
        {
            using var wb = new XLWorkbook();
            var summary  = summaryResult.Value!;

            // ── Sheet 1: Rankings ────────────────────────────────────────
            var ws1 = wb.Worksheets.Add("Rankings");
            ws1.Cell(1, 1).Value = "Rank";
            ws1.Cell(1, 2).Value = "Project";
            ws1.Cell(1, 3).Value = "Category";
            ws1.Cell(1, 4).Value = "Mean Score";
            ws1.Cell(1, 5).Value = "Max Score";
            ws1.Cell(1, 6).Value = "Min Score";
            ws1.Cell(1, 7).Value = "Votes";

            // Style header row
            var headerRange1 = ws1.Range(1, 1, 1, 7);
            headerRange1.Style.Font.Bold = true;
            headerRange1.Style.Fill.BackgroundColor = XLColor.FromHtml("#007AFF");
            headerRange1.Style.Font.FontColor = XLColor.White;

            for (int i = 0; i < summary.Rankings.Count; i++)
            {
                var r = summary.Rankings[i];
                int row = i + 2;
                ws1.Cell(row, 1).Value = i + 1;
                ws1.Cell(row, 2).Value = r.ProjectName;
                ws1.Cell(row, 3).Value = r.Category;
                ws1.Cell(row, 4).Value = r.MeanScore;
                ws1.Cell(row, 5).Value = r.MaxScore;
                ws1.Cell(row, 6).Value = r.MinScore;
                ws1.Cell(row, 7).Value = r.VoteCount;

                // Alternate row shading
                if (i % 2 == 0)
                    ws1.Range(row, 1, row, 7).Style.Fill.BackgroundColor = XLColor.FromHtml("#F2F2F7");
            }
            ws1.Columns().AdjustToContents();

            // ── Sheet 2: Detail ──────────────────────────────────────────
            if (votesResult.IsOk && judgesResult.IsOk && projResult.IsOk)
            {
                var ws2 = wb.Worksheets.Add("Vote Detail");
                ws2.Cell(1, 1).Value = "Project";
                ws2.Cell(1, 2).Value = "Judge";
                ws2.Cell(1, 3).Value = "Weighted Score";
                ws2.Cell(1, 4).Value = "Signature Valid";
                ws2.Cell(1, 5).Value = "Received At";

                var headerRange2 = ws2.Range(1, 1, 1, 5);
                headerRange2.Style.Font.Bold = true;
                headerRange2.Style.Fill.BackgroundColor = XLColor.FromHtml("#007AFF");
                headerRange2.Style.Font.FontColor = XLColor.White;

                var judgeMap   = judgesResult.Value!.ToDictionary(j => j.Id,   j => j.Name);
                var projectMap = projResult.Value!.ToDictionary(p => p.Id, p => p.Name);

                int row = 2;
                foreach (var v in votesResult.Value!.OrderBy(v => v.ProjectId).ThenBy(v => v.JudgeId))
                {
                    ws2.Cell(row, 1).Value = projectMap.GetValueOrDefault(v.ProjectId, $"#{v.ProjectId}");
                    ws2.Cell(row, 2).Value = judgeMap.GetValueOrDefault(v.JudgeId,   $"#{v.JudgeId}");
                    ws2.Cell(row, 3).Value = v.WeightedScore;
                    ws2.Cell(row, 4).Value = !string.IsNullOrEmpty(v.SignatureBase64) ? "✓" : "—";
                    ws2.Cell(row, 5).Value = v.ReceivedAt;
                    row++;
                }
                ws2.Columns().AdjustToContents();
            }

            Directory.CreateDirectory(outputDirectory);
            string fileName = $"Results_Final_{summary.EventName.Replace(' ', '_')}_{DateTime.Now:yyyyMMdd_HHmm}.xlsx";
            string filePath = Path.Combine(outputDirectory, fileName);
            wb.SaveAs(filePath);

            return Result<string>.Ok(filePath);
        }
        catch (Exception ex)
        {
            return Result<string>.Fail($"Excel export failed: {ex.Message}");
        }
    }

    public async Task<Result<string>> ExportProjectsAsync(int eventId, string outputDirectory)
    {
        var projectsResult = await _projects.GetByEventAsync(eventId);
        if (projectsResult.IsFail) return Result<string>.Fail(projectsResult.Error!);

        try
        {
            using var wb = new XLWorkbook();
            var ws = wb.Worksheets.Add("Projects");

            ws.Cell(1, 1).Value = "Name";
            ws.Cell(1, 2).Value = "Category";
            ws.Cell(1, 3).Value = "Description";
            ws.Cell(1, 4).Value = "TeamMembers";
            ws.Cell(1, 5).Value = "StandNumber";
            ws.Cell(1, 6).Value = "GithubLink";
            ws.Cell(1, 7).Value = "VideoLink";
            ws.Cell(1, 8).Value = "SpeechVideoLink";
            ws.Cell(1, 9).Value = "TechStack";
            ws.Cell(1, 10).Value = "Objetivos";
            ws.Cell(1, 11).Value = "ProjectCode";
            ws.Cell(1, 12).Value = "SortOrder";
            ws.Cell(1, 13).Value = "SequenceNumber";
            ws.Cell(1, 14).Value = "EditToken";
            ws.Cell(1, 15).Value = "CreatedAt";

            var headerRange = ws.Range(1, 1, 1, 15);
            headerRange.Style.Font.Bold = true;
            headerRange.Style.Fill.BackgroundColor = XLColor.FromHtml("#007AFF");
            headerRange.Style.Font.FontColor = XLColor.White;

            var projects = projectsResult.Value!;
            for (int i = 0; i < projects.Count; i++)
            {
                var p = projects[i];
                int row = i + 2;

                ws.Cell(row, 1).Value = p.Name;
                ws.Cell(row, 2).Value = p.Category;
                ws.Cell(row, 3).Value = p.Description;
                ws.Cell(row, 4).Value = p.TeamMembers;
                ws.Cell(row, 5).Value = p.StandNumber;
                ws.Cell(row, 6).Value = p.GithubLink;
                ws.Cell(row, 7).Value = p.VideoLink;
                ws.Cell(row, 8).Value = p.SpeechVideoLink;
                ws.Cell(row, 9).Value = p.TechStack;
                ws.Cell(row, 10).Value = p.Objetivos;
                ws.Cell(row, 11).Value = p.ProjectCode;
                ws.Cell(row, 12).Value = p.SortOrder;
                ws.Cell(row, 13).Value = p.SequenceNumber;
                ws.Cell(row, 14).Value = p.EditToken;
                ws.Cell(row, 15).Value = p.CreatedAt;

                if (i % 2 == 0)
                    ws.Range(row, 1, row, 15).Style.Fill.BackgroundColor = XLColor.FromHtml("#F2F2F7");
            }

            ws.Columns().AdjustToContents();

            Directory.CreateDirectory(outputDirectory);
            string fileName = $"Projects_Export_Event_{eventId}_{DateTime.Now:yyyyMMdd_HHmm}.xlsx";
            string filePath = Path.Combine(outputDirectory, fileName);
            wb.SaveAs(filePath);

            return Result<string>.Ok(filePath);
        }
        catch (Exception ex)
        {
            return Result<string>.Fail($"Project export failed: {ex.Message}");
        }
    }

    public async Task<Result<ProjectImportResultDto>> ImportProjectsAsync(string filePath, int targetEventId, bool replaceExisting)
    {
        if (!File.Exists(filePath))
            return Result<ProjectImportResultDto>.Fail("El archivo seleccionado no existe.");

        try
        {
            using var wb = new XLWorkbook(filePath);
            var ws = wb.Worksheets.FirstOrDefault(w => string.Equals(w.Name, "Projects", StringComparison.OrdinalIgnoreCase))
                     ?? wb.Worksheets.FirstOrDefault();

            if (ws is null)
                return Result<ProjectImportResultDto>.Fail("El archivo no contiene hojas válidas.");

            var headerRow = ws.FirstRowUsed();
            if (headerRow is null)
                return Result<ProjectImportResultDto>.Fail("La hoja está vacía.");

            var headers = headerRow.CellsUsed()
                .ToDictionary(
                    c => c.GetString().Trim(),
                    c => c.Address.ColumnNumber,
                    StringComparer.OrdinalIgnoreCase);

            if (!headers.ContainsKey("Name"))
                return Result<ProjectImportResultDto>.Fail("No se encontró la columna obligatoria 'Name'.");

            if (replaceExisting)
            {
                var deleteResult = await _projects.DeleteByEventAsync(targetEventId);
                if (deleteResult.IsFail)
                    return Result<ProjectImportResultDto>.Fail(deleteResult.Error!);
            }

            var existingResult = await _projects.GetByEventAsync(targetEventId);
            if (existingResult.IsFail)
                return Result<ProjectImportResultDto>.Fail(existingResult.Error!);

            var usedCodes = new HashSet<string>(
                existingResult.Value!
                    .Select(p => p.ProjectCode)
                    .Where(code => !string.IsNullOrWhiteSpace(code)),
                StringComparer.OrdinalIgnoreCase);

            int imported = 0;
            int skipped = 0;
            int nextSortOrder = (existingResult.Value!.Count > 0 ? existingResult.Value!.Max(p => p.SortOrder) : 0) + 1;
            int nextCode = ExtractMaxProjectCodeNumber(usedCodes) + 1;

            var buffer = new List<Project>();
            foreach (var row in ws.RowsUsed().Skip(1))
            {
                var name = GetValue(row, headers, "Name");
                if (string.IsNullOrWhiteSpace(name))
                {
                    skipped++;
                    continue;
                }

                var code = GetValue(row, headers, "ProjectCode");
                if (string.IsNullOrWhiteSpace(code) || usedCodes.Contains(code))
                {
                    code = $"PROJ-{nextCode:D3}";
                    nextCode++;
                }

                usedCodes.Add(code);

                var item = new Project
                {
                    EventId = targetEventId,
                    Name = name,
                    Category = GetValue(row, headers, "Category"),
                    Description = GetValue(row, headers, "Description"),
                    TeamMembers = GetValue(row, headers, "TeamMembers"),
                    StandNumber = GetValue(row, headers, "StandNumber"),
                    GithubLink = GetValue(row, headers, "GithubLink"),
                    VideoLink = GetValue(row, headers, "VideoLink"),
                    SpeechVideoLink = GetValue(row, headers, "SpeechVideoLink"),
                    TechStack = GetValue(row, headers, "TechStack"),
                    Objetivos = GetValue(row, headers, "Objetivos"),
                    ProjectCode = code,
                    SortOrder = ReadInt(row, headers, "SortOrder") ?? nextSortOrder,
                    SequenceNumber = 0,
                    EditToken = string.IsNullOrWhiteSpace(GetValue(row, headers, "EditToken"))
                        ? Guid.NewGuid().ToString("N")
                        : GetValue(row, headers, "EditToken"),
                    CreatedAt = string.IsNullOrWhiteSpace(GetValue(row, headers, "CreatedAt"))
                        ? DateTime.UtcNow.ToString("O")
                        : GetValue(row, headers, "CreatedAt")
                };

                buffer.Add(item);
                imported++;
                nextSortOrder = Math.Max(nextSortOrder + 1, item.SortOrder + 1);
            }

            if (buffer.Count > 0)
            {
                var insertResult = await _projects.BulkInsertAsync(buffer);
                if (insertResult.IsFail)
                    return Result<ProjectImportResultDto>.Fail(insertResult.Error!);
            }

            return Result<ProjectImportResultDto>.Ok(new ProjectImportResultDto
            {
                ImportedCount = imported,
                SkippedCount = skipped,
                TargetEventId = targetEventId
            });
        }
        catch (Exception ex)
        {
            return Result<ProjectImportResultDto>.Fail($"Project import failed: {ex.Message}");
        }
    }

    private static string GetValue(IXLRow row, IReadOnlyDictionary<string, int> headers, string name)
    {
        if (!headers.TryGetValue(name, out var col)) return string.Empty;
        return row.Cell(col).GetString().Trim();
    }

    private static int? ReadInt(IXLRow row, IReadOnlyDictionary<string, int> headers, string name)
    {
        var raw = GetValue(row, headers, name);
        if (int.TryParse(raw, out var value)) return value;
        return null;
    }

    private static int ExtractMaxProjectCodeNumber(IEnumerable<string> codes)
    {
        int max = 0;
        foreach (var code in codes)
        {
            var tail = new string(code.Reverse().TakeWhile(char.IsDigit).Reverse().ToArray());
            if (int.TryParse(tail, out var parsed))
                max = Math.Max(max, parsed);
        }

        return max;
    }
}

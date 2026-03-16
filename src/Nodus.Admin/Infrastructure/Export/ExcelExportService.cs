using ClosedXML.Excel;
using Nodus.Admin.Application.Interfaces.Persistence;
using Nodus.Admin.Application.Interfaces.Services;
using Nodus.Admin.Application.UseCases.Votes;
using Nodus.Admin.Domain.Common;

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
}

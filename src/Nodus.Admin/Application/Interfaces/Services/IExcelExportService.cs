using Nodus.Admin.Domain.Common;
using Nodus.Admin.Application.DTOs;

namespace Nodus.Admin.Application.Interfaces.Services;

/// <summary>
/// Exports event results to Results_Final.xlsx (Decision #55).
/// </summary>
public interface IExcelExportService
{
    /// <summary>
    /// Export a summary spreadsheet for the given event.
    /// Returns the full file path of the created .xlsx.
    /// </summary>
    Task<Result<string>> ExportAsync(int eventId, string outputDirectory);

    /// <summary>
    /// Export all projects for the given event to a spreadsheet.
    /// Returns the full file path of the created .xlsx.
    /// </summary>
    Task<Result<string>> ExportProjectsAsync(int eventId, string outputDirectory);

    /// <summary>
    /// Import projects from a spreadsheet into the target event.
    /// </summary>
    Task<Result<ProjectImportResultDto>> ImportProjectsAsync(string filePath, int targetEventId, bool replaceExisting);
}

using Nodus.Admin.Domain.Common;

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
}

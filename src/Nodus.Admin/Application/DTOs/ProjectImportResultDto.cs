namespace Nodus.Admin.Application.DTOs;

public sealed class ProjectImportResultDto
{
    public int ImportedCount { get; init; }
    public int SkippedCount { get; init; }
    public int TargetEventId { get; init; }
}

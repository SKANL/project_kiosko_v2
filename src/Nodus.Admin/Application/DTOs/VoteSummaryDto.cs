namespace Nodus.Admin.Application.DTOs;

/// <summary>
/// Aggregated score summary for one project — displayed on the results page and exported to Excel.
/// </summary>
public sealed record ProjectScoreDto(
    int    ProjectId,
    string ProjectName,
    string Category,
    int    VoteCount,
    double MeanScore,      // Average of all judges' weighted scores
    double MaxScore,
    double MinScore
);

/// <summary>Full results summary for an event.</summary>
public sealed record VoteSummaryDto(
    int                    EventId,
    string                 EventName,
    IReadOnlyList<ProjectScoreDto> Rankings  // Sorted descending by MeanScore
);

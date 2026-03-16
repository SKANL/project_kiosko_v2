using Nodus.Admin.Application.DTOs;
using Nodus.Admin.Application.Interfaces.Persistence;
using Nodus.Admin.Domain.Common;

namespace Nodus.Admin.Application.UseCases.Votes;

/// <summary>
/// Aggregates votes for an event and produces a ranked VoteSummaryDto.
/// Scoring formula (Decision #55): weighted average per judge, then mean across judges.
/// </summary>
public sealed class GetVoteSummaryUseCase
{
    private readonly IVoteRepository    _votes;
    private readonly IProjectRepository _projects;
    private readonly IEventRepository   _events;

    public GetVoteSummaryUseCase(
        IVoteRepository    votes,
        IProjectRepository projects,
        IEventRepository   events)
    {
        _votes    = votes;
        _projects = projects;
        _events   = events;
    }

    public async Task<Result<VoteSummaryDto>> ExecuteAsync(int eventId)
    {
        var eventResult   = await _events.GetByIdAsync(eventId);
        if (eventResult.IsFail) return Result<VoteSummaryDto>.Fail(eventResult.Error!);
        if (eventResult.Value is null) return Result<VoteSummaryDto>.Fail($"Event {eventId} not found");

        var votesResult   = await _votes.GetByEventAsync(eventId);
        if (votesResult.IsFail) return Result<VoteSummaryDto>.Fail(votesResult.Error!);

        var projectsResult = await _projects.GetByEventAsync(eventId);
        if (projectsResult.IsFail) return Result<VoteSummaryDto>.Fail(projectsResult.Error!);

        // Group votes by project
        var votesByProject = votesResult.Value!
            .GroupBy(v => v.ProjectId)
            .ToDictionary(g => g.Key, g => g.ToList());

        var rankings = projectsResult.Value!
            .Select(p =>
            {
                bool hasVotes = votesByProject.TryGetValue(p.Id, out var pvotes) && pvotes!.Count > 0;
                double mean   = hasVotes ? pvotes!.Average(v => v.WeightedScore) : 0.0;
                double max    = hasVotes ? pvotes!.Max(v => v.WeightedScore) : 0.0;
                double min    = hasVotes ? pvotes!.Min(v => v.WeightedScore) : 0.0;

                return new ProjectScoreDto(
                    p.Id,
                    p.Name,
                    p.Category,
                    hasVotes ? pvotes!.Count : 0,
                    Math.Round(mean, 2),
                    Math.Round(max,  2),
                    Math.Round(min,  2)
                );
            })
            .OrderByDescending(r => r.MeanScore)
            .ToList();

        return Result<VoteSummaryDto>.Ok(new VoteSummaryDto(
            eventId,
            eventResult.Value.Name,
            rankings));
    }
}

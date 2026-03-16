using MongoDB.Driver;
using Nodus.API.Models;
using Nodus.API.Services;

namespace Nodus.API.Endpoints;

// ── Scoring Formula Helpers ───────────────────────────────────────────────
// Doc 14 §3: Decision #55 canonical formula.

file static class ScoringFormulas
{
    /// <summary>
    /// Weighted average for a single judge's vote.
    /// judgeScore = Σ(sᵢ × wᵢ) / Σwᵢ
    /// </summary>
    public static double JudgeScore(
        Dictionary<string, double> scores,
        Dictionary<string, double> weights)
    {
        double numerator   = 0, denominator = 0;
        foreach (var (id, score) in scores)
        {
            var w = weights.GetValueOrDefault(id, 1.0);
            numerator   += score * w;
            denominator += w;
        }
        return denominator == 0 ? 0 : numerator / denominator;
    }

    /// <summary>
    /// Raw weighted sum (used as tie-breaker).
    /// TotalPoints = Σⱼ Σᵢ (sᵢⱼ × wᵢ)
    /// </summary>
    public static double TotalPoints(
        IEnumerable<(Dictionary<string, double> scores, Dictionary<string, double> weights)> judgeVotes)
    {
        double total = 0;
        foreach (var (sc, wt) in judgeVotes)
            foreach (var (id, score) in sc)
                total += score * wt.GetValueOrDefault(id, 1.0);
        return total;
    }
}

// ── DTOs ─────────────────────────────────────────────────────────────────

public sealed record ProjectResultDto(
    string  ProjectId,
    string  ProjectName,
    string  Category,
    double  AverageScore,
    double  TotalPoints,
    int     JudgeCount,
    int     Rank);

public sealed record EventResultsDto(
    string EventId,
    string EventName,
    string RubricJson,
    List<ProjectResultDto> Projects);

// ── Endpoint Registration ─────────────────────────────────────────────────

public static class ResultsEndpoints
{
    public static void Map(WebApplication app)
    {
        var grp = app.MapGroup("/api/results");

        // GET /api/results/{eventId}
        // Returns ranked project results using Decision #55 scoring formula.
        // No auth required — results are public after event ends.
        grp.MapGet("/{eventId}", async (string eventId, MongoDbService mongo) =>
        {
            var eventDoc = await mongo.Events
                .Find(Builders<EventDocument>.Filter.Eq(e => e.Id, eventId))
                .FirstOrDefaultAsync();

            if (eventDoc is null)
                return Results.NotFound(new { error = $"Event '{eventId}' not found." });

            // Load all votes for this event
            var allVotes = await mongo.Votes
                .Find(Builders<VoteDocument>.Filter.Eq(v => v.EventId, eventId))
                .ToListAsync();

            // Load all projects
            var projects = await mongo.Projects
                .Find(Builders<ProjectDocument>.Filter.Eq(p => p.EventId, eventId))
                .ToListAsync();

            // Latest Version Wins: per (judge, project) keep highest version
            var latestVotes = allVotes
                .GroupBy(v => (v.JudgeId, v.ProjectId))
                .Select(g => g.MaxBy(v => v.Version)!)
                .ToList();

            // Compute scores per project
            var projectResults = new List<ProjectResultDto>();
            foreach (var project in projects)
            {
                var votes = latestVotes.Where(v => v.ProjectId == project.Id).ToList();
                if (votes.Count == 0) continue;

                var judgeVotes = votes.Select(v =>
                    (
                        System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, double>>(v.ScoresJson)  ?? new(),
                        System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, double>>(v.WeightsJson) ?? new()
                    )).ToList();

                var judgeScores  = judgeVotes.Select(jv => ScoringFormulas.JudgeScore(jv.Item1, jv.Item2)).ToList();
                var average      = judgeScores.Average();
                var totalPoints  = ScoringFormulas.TotalPoints(judgeVotes);

                projectResults.Add(new ProjectResultDto(
                    project.Id, project.Name, project.Category,
                    Math.Round(average, 2), Math.Round(totalPoints, 2),
                    judgeScores.Count, 0));
            }

            // Rank: by AverageScore desc, then TotalPoints desc (tie-breaker)
            projectResults = projectResults
                .OrderByDescending(p => p.AverageScore)
                .ThenByDescending(p => p.TotalPoints)
                .ToList();

            var ranked = projectResults
                .Select((p, i) => p with { Rank = i + 1 })
                .ToList();

            return Results.Ok(new EventResultsDto(eventDoc.Id, eventDoc.Name, eventDoc.RubricJson, ranked));
        })
        .WithName("GetEventResults")
        .WithSummary("Get ranked project results for an event using Decision #55 formula.")
        .Produces<EventResultsDto>()
        .ProducesProblem(404);
    }
}

using MongoDB.Driver;
using Nodus.API.Models;
using Nodus.API.Services;

namespace Nodus.API.Endpoints;

// ── DTOs ─────────────────────────────────────────────────────────────────

public sealed class SyncEventRequest
{
    public EventDocument Event    { get; set; } = new();
    public List<ProjectDocument> Projects { get; set; } = new();
    public List<JudgeDocument>   Judges   { get; set; } = new();
}

public sealed record SyncEventResponse(string EventId, int ProjectsUpserted, int JudgesUpserted);

public sealed class SyncVotesRequest
{
    public string          EventId { get; set; } = string.Empty;
    public List<VoteDocument> Votes { get; set; } = new();
}

public sealed record SyncVotesResponse(int Upserted, int Skipped);

// ── Endpoint Registration ─────────────────────────────────────────────────

public static class SyncEndpoints
{
    public static void Map(WebApplication app)
    {
        var grp = app.MapGroup("/api/sync").RequireAuthorization();

        // POST /api/sync/event
        // Admin calls this after the event ends (or periodically) to push full state to cloud.
        grp.MapPost("/event", async (SyncEventRequest req, MongoDbService mongo) =>
        {
            // Upsert event
            await mongo.Events.ReplaceOneAsync(
                Builders<EventDocument>.Filter.Eq(e => e.Id, req.Event.Id),
                req.Event,
                new ReplaceOptions { IsUpsert = true });

            // Upsert projects
            int projectsUpserted = 0;
            foreach (var project in req.Projects)
            {
                await mongo.Projects.ReplaceOneAsync(
                    Builders<ProjectDocument>.Filter.Eq(p => p.Id, project.Id),
                    project,
                    new ReplaceOptions { IsUpsert = true });
                projectsUpserted++;
            }

            // Upsert judges
            int judgesUpserted = 0;
            foreach (var judge in req.Judges)
            {
                await mongo.Judges.ReplaceOneAsync(
                    Builders<JudgeDocument>.Filter.Eq(j => j.Id, judge.Id),
                    judge,
                    new ReplaceOptions { IsUpsert = true });
                judgesUpserted++;
            }

            return Results.Ok(new SyncEventResponse(req.Event.Id, projectsUpserted, judgesUpserted));
        })
        .WithName("SyncEvent")
        .WithSummary("Admin: push full event state to cloud (MongoDB).")
        .Produces<SyncEventResponse>()
        .ProducesProblem(401);

        // POST /api/sync/votes
        // Admin calls this in batches to push received votes to cloud.
        // Server uses Latest Version Wins (Decision #34): only upserts if incoming version >= stored.
        grp.MapPost("/votes", async (SyncVotesRequest req, MongoDbService mongo) =>
        {
            int upserted = 0, skipped = 0;

            foreach (var vote in req.Votes)
            {
                var existing = await mongo.Votes
                    .Find(Builders<VoteDocument>.Filter.And(
                        Builders<VoteDocument>.Filter.Eq(v => v.EventId,   vote.EventId),
                        Builders<VoteDocument>.Filter.Eq(v => v.ProjectId, vote.ProjectId),
                        Builders<VoteDocument>.Filter.Eq(v => v.JudgeId,   vote.JudgeId)))
                    .SortByDescending(v => v.Version)
                    .FirstOrDefaultAsync();

                if (existing is not null && existing.Version >= vote.Version)
                {
                    skipped++;
                    continue;
                }

                await mongo.Votes.ReplaceOneAsync(
                    Builders<VoteDocument>.Filter.Eq(v => v.Id, vote.Id),
                    vote,
                    new ReplaceOptions { IsUpsert = true });
                upserted++;
            }

            return Results.Ok(new SyncVotesResponse(upserted, skipped));
        })
        .WithName("SyncVotes")
        .WithSummary("Admin: push batch of votes to cloud. Latest Version Wins (Decision #34).")
        .Produces<SyncVotesResponse>()
        .ProducesProblem(401);
    }
}

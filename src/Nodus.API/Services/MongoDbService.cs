using MongoDB.Driver;
using Nodus.API.Models;

namespace Nodus.API.Services;

/// <summary>
/// Singleton MongoDB connection manager. All collections exposed as typed properties.
/// </summary>
public sealed class MongoDbService
{
    private readonly IMongoDatabase _db;

    public MongoDbService(IConfiguration config)
    {
        var connectionString = config["MongoDB:ConnectionString"]
            ?? throw new InvalidOperationException("MongoDB:ConnectionString is not configured.");
        var databaseName     = config["MongoDB:DatabaseName"] ?? "nodus";

        var client = new MongoClient(connectionString);
        _db = client.GetDatabase(databaseName);

        // Ensure indexes on first startup
        EnsureIndexes();
    }

    public IMongoCollection<EventDocument>    Events    => _db.GetCollection<EventDocument>("events");
    public IMongoCollection<ProjectDocument>  Projects  => _db.GetCollection<ProjectDocument>("projects");
    public IMongoCollection<VoteDocument>     Votes     => _db.GetCollection<VoteDocument>("votes");
    public IMongoCollection<JudgeDocument>    Judges    => _db.GetCollection<JudgeDocument>("judges");

    private void EnsureIndexes()
    {
        // Votes: queried heavily by eventId + projectId + judgeId
        Votes.Indexes.CreateOne(new CreateIndexModel<VoteDocument>(
            Builders<VoteDocument>.IndexKeys
                .Ascending(v => v.EventId)
                .Ascending(v => v.ProjectId)
                .Ascending(v => v.JudgeId)));

        // Projects: queried by eventId
        Projects.Indexes.CreateOne(new CreateIndexModel<ProjectDocument>(
            Builders<ProjectDocument>.IndexKeys.Ascending(p => p.EventId)));

        // Judges: queried by eventId
        Judges.Indexes.CreateOne(new CreateIndexModel<JudgeDocument>(
            Builders<JudgeDocument>.IndexKeys.Ascending(j => j.EventId)));
    }
}

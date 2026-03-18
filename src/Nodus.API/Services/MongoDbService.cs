using MongoDB.Driver;
using Nodus.API.Models;

namespace Nodus.API.Services;

/// <summary>
/// Singleton MongoDB connection manager. All collections exposed as typed properties.
/// MongoDB auto-creates collections on first insert — no manual setup needed.
/// EnsureIndexes is best-effort: failure is logged but doesn't crash the API.
/// </summary>
public sealed class MongoDbService
{
    private readonly IMongoDatabase _db;

    public MongoDbService(IConfiguration config)
    {
        var connectionString = config["MongoDB:ConnectionString"]
            ?? config["MongoDB__ConnectionString"]
            ?? throw new InvalidOperationException("MongoDB:ConnectionString is not configured.");
        var databaseName     = config["MongoDB:DatabaseName"] ?? config["MongoDB__DatabaseName"] ?? "nodus";

        var settings = MongoClientSettings.FromConnectionString(connectionString);
        settings.ServerSelectionTimeout = TimeSpan.FromSeconds(10);
        
        var client = new MongoClient(settings);
        _db = client.GetDatabase(databaseName);

        // Best-effort index creation — MongoDB creates collections automatically on first write
        _ = Task.Run(EnsureIndexesAsync);
    }

    public IMongoCollection<EventDocument>    Events    => _db.GetCollection<EventDocument>("events");
    public IMongoCollection<ProjectDocument>  Projects  => _db.GetCollection<ProjectDocument>("projects");
    public IMongoCollection<VoteDocument>     Votes     => _db.GetCollection<VoteDocument>("votes");
    public IMongoCollection<JudgeDocument>    Judges    => _db.GetCollection<JudgeDocument>("judges");

    private async Task EnsureIndexesAsync()
    {
        try
        {
            // Votes: queried heavily by eventId + projectId + judgeId
            await Votes.Indexes.CreateOneAsync(new CreateIndexModel<VoteDocument>(
                Builders<VoteDocument>.IndexKeys
                    .Ascending(v => v.EventId)
                    .Ascending(v => v.ProjectId)
                    .Ascending(v => v.JudgeId)));

            // Projects: queried by eventId
            await Projects.Indexes.CreateOneAsync(new CreateIndexModel<ProjectDocument>(
                Builders<ProjectDocument>.IndexKeys.Ascending(p => p.EventId)));

            // Judges: queried by eventId
            await Judges.Indexes.CreateOneAsync(new CreateIndexModel<JudgeDocument>(
                Builders<JudgeDocument>.IndexKeys.Ascending(j => j.EventId)));

            Console.WriteLine("[MongoDbService] Indexes ensured OK.");
        }
        catch (Exception ex)
        {
            // Non-fatal — indexes will be created on the next startup or can be added manually
            Console.WriteLine($"[MongoDbService] Warning: could not ensure indexes: {ex.Message}");
        }
    }
}

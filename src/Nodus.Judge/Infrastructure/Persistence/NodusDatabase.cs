using Nodus.Judge.Domain.Entities;
using SQLite;

namespace Nodus.Judge.Infrastructure.Persistence;

/// <summary>
/// Manages the SQLite connection for the Judge app. Singleton.
/// </summary>
public sealed class NodusDatabase : IDisposable
{
    private readonly SQLiteAsyncConnection _db;

    public NodusDatabase(string dbPath)
    {
        _db = new SQLiteAsyncConnection(dbPath,
            SQLiteOpenFlags.ReadWrite | SQLiteOpenFlags.Create | SQLiteOpenFlags.SharedCache);
    }

    public SQLiteAsyncConnection Connection => _db;

    public async Task InitializeAsync()
    {
        await _db.CreateTableAsync<LocalEvent>();
        await _db.CreateTableAsync<LocalProject>();
        await _db.CreateTableAsync<LocalJudge>();
        await _db.CreateTableAsync<LocalVote>();

        // Additive schema migrations — silently ignored if column already exists.
        await ApplyMigrationAsync("ALTER TABLE local_events  ADD COLUMN RubricJson  TEXT NOT NULL DEFAULT ''");
        await ApplyMigrationAsync("ALTER TABLE local_events  ADD COLUMN RubricVersion INTEGER NOT NULL DEFAULT 1");
        await ApplyMigrationAsync("ALTER TABLE local_events  ADD COLUMN FinishedAt  TEXT NOT NULL DEFAULT ''");
        await ApplyMigrationAsync("ALTER TABLE local_events  ADD COLUMN GraceEndsAt TEXT NOT NULL DEFAULT ''");
        await ApplyMigrationAsync("ALTER TABLE local_votes   ADD COLUMN Version     INTEGER NOT NULL DEFAULT 1");
        await ApplyMigrationAsync("ALTER TABLE local_votes   ADD COLUMN PacketId    TEXT NOT NULL DEFAULT ''");
        await ApplyMigrationAsync("ALTER TABLE local_votes   ADD COLUMN HopPathJson TEXT NOT NULL DEFAULT '[]'");
        await ApplyMigrationAsync("ALTER TABLE local_votes   ADD COLUMN RemainingTtl INTEGER NOT NULL DEFAULT 0");
        await ApplyMigrationAsync("ALTER TABLE local_projects ADD COLUMN SequenceNumber INTEGER NOT NULL DEFAULT 0");
    }

    private async Task ApplyMigrationAsync(string ddl)
    {
        try   { await _db.ExecuteAsync(ddl); }
        catch (Exception ex) when (ex.Message.Contains("duplicate column", StringComparison.OrdinalIgnoreCase)) { }
    }

    public void Dispose() => _db.CloseAsync().GetAwaiter().GetResult();
}

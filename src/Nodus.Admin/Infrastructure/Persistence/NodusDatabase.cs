using Nodus.Admin.Domain.Entities;
using SQLite;

namespace Nodus.Admin.Infrastructure.Persistence;

/// <summary>
/// Manages the SQLite connection and applies schema migrations.
/// Registered as a singleton in DI.
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

    /// <summary>Create tables if they don't exist. Call during app startup.</summary>
    public async Task InitializeAsync()
    {
        await _db.CreateTableAsync<NodusEvent>();
        await _db.CreateTableAsync<Project>();
        await _db.CreateTableAsync<Judge>();
        await _db.CreateTableAsync<Vote>();

        // Additive schema migrations — run every startup, silently ignored if column already exists.
        await ApplyMigrationAsync("ALTER TABLE events  ADD COLUMN RubricJson  TEXT NOT NULL DEFAULT ''");
        await ApplyMigrationAsync("ALTER TABLE events  ADD COLUMN RubricVersion INTEGER NOT NULL DEFAULT 1");
        await ApplyMigrationAsync("ALTER TABLE events  ADD COLUMN FinishedAt  TEXT NOT NULL DEFAULT ''");
        await ApplyMigrationAsync("ALTER TABLE events  ADD COLUMN GraceEndsAt TEXT NOT NULL DEFAULT ''");
        await ApplyMigrationAsync("ALTER TABLE events  ADD COLUMN SharedKeyBase64 TEXT NOT NULL DEFAULT ''");
        await ApplyMigrationAsync("ALTER TABLE events  ADD COLUMN AccessQrPayload TEXT NOT NULL DEFAULT ''");
        await ApplyMigrationAsync("ALTER TABLE judges  ADD COLUMN IsBlocked   INTEGER NOT NULL DEFAULT 0");
        await ApplyMigrationAsync("ALTER TABLE votes   ADD COLUMN Version     INTEGER NOT NULL DEFAULT 1");
        await ApplyMigrationAsync("ALTER TABLE votes   ADD COLUMN PacketId    TEXT NOT NULL DEFAULT ''");
        await ApplyMigrationAsync("ALTER TABLE votes   ADD COLUMN HopPathJson TEXT NOT NULL DEFAULT '[]'");
        await ApplyMigrationAsync("ALTER TABLE votes   ADD COLUMN RemainingTtl INTEGER NOT NULL DEFAULT 0");
        await ApplyMigrationAsync("ALTER TABLE projects ADD COLUMN ProjectCode TEXT NOT NULL DEFAULT ''");
        await ApplyMigrationAsync("ALTER TABLE projects ADD COLUMN SequenceNumber INTEGER NOT NULL DEFAULT 0");
    }

    /// <summary>Deletes all data from the database.</summary>
    public async Task PurgeAllDataAsync()
    {
        await _db.DropTableAsync<Vote>();
        await _db.DropTableAsync<Judge>();
        await _db.DropTableAsync<Project>();
        await _db.DropTableAsync<NodusEvent>();
        await InitializeAsync();
    }

    /// <summary>Run a DDL statement, suppressing the 'duplicate column' error from SQLite.</summary>
    private async Task ApplyMigrationAsync(string ddl)
    {
        try   { await _db.ExecuteAsync(ddl); }
        catch (Exception ex) when (ex.Message.Contains("duplicate column", StringComparison.OrdinalIgnoreCase)) { }
    }

    public void Dispose() => _db.CloseAsync().GetAwaiter().GetResult();
}

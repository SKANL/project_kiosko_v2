using Nodus.Judge.Application.Interfaces.Persistence;
using Nodus.Judge.Domain.Common;
using Nodus.Judge.Domain.Entities;

namespace Nodus.Judge.Infrastructure.Persistence;

public sealed class LocalEventRepository : ILocalEventRepository
{
    private readonly NodusDatabase _db;
    public LocalEventRepository(NodusDatabase db) => _db = db;

    public async Task<Result<LocalEvent?>> GetActiveAsync()
    {
        try
        {
            var evt = await _db.Connection.Table<LocalEvent>()
                .OrderByDescending(e => e.RemoteId)
                .FirstOrDefaultAsync();
            return Result<LocalEvent?>.Ok(evt);
        }
        catch (Exception ex) { return Result<LocalEvent?>.Fail(ex.Message); }
    }

    public async Task<Result<LocalEvent>> GetByIdAsync(int remoteId)
    {
        try
        {
            var evt = await _db.Connection.FindAsync<LocalEvent>(remoteId);
            return evt is null ? Result<LocalEvent>.Fail($"Event {remoteId} not found") : Result<LocalEvent>.Ok(evt);
        }
        catch (Exception ex) { return Result<LocalEvent>.Fail(ex.Message); }
    }

    public async Task<Result> UpsertAsync(LocalEvent evt)
    {
        try
        {
            await _db.Connection.InsertOrReplaceAsync(evt);
            return Result.Ok();
        }
        catch (Exception ex) { return Result.Fail(ex.Message); }
    }

    public async Task<Result> DeleteAllAsync()
    {
        try { await _db.Connection.ExecuteAsync("DELETE FROM local_events"); return Result.Ok(); }
        catch (Exception ex) { return Result.Fail(ex.Message); }
    }
}

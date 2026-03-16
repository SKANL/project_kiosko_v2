using Nodus.Judge.Application.Interfaces.Persistence;
using Nodus.Judge.Domain.Common;
using Nodus.Judge.Domain.Entities;

namespace Nodus.Judge.Infrastructure.Persistence;

public sealed class LocalJudgeRepository : ILocalJudgeRepository
{
    private readonly NodusDatabase _db;
    public LocalJudgeRepository(NodusDatabase db) => _db = db;

    public async Task<Result<LocalJudge?>> GetSelfAsync()
    {
        try
        {
            var j = await _db.Connection.Table<LocalJudge>()
                .Where(x => x.IsSelf)
                .FirstOrDefaultAsync();
            return Result<LocalJudge?>.Ok(j);
        }
        catch (Exception ex) { return Result<LocalJudge?>.Fail(ex.Message); }
    }

    public async Task<Result<IReadOnlyList<LocalJudge>>> GetByEventAsync(int eventId)
    {
        try
        {
            var list = await _db.Connection.Table<LocalJudge>()
                .Where(j => j.EventId == eventId).ToListAsync();
            return Result<IReadOnlyList<LocalJudge>>.Ok(list);
        }
        catch (Exception ex) { return Result<IReadOnlyList<LocalJudge>>.Fail(ex.Message); }
    }

    public async Task<Result<LocalJudge?>> GetByPublicKeyAsync(string publicKeyBase64)
    {
        try
        {
            var j = await _db.Connection.Table<LocalJudge>()
                .Where(x => x.PublicKeyBase64 == publicKeyBase64)
                .FirstOrDefaultAsync();
            return Result<LocalJudge?>.Ok(j);
        }
        catch (Exception ex) { return Result<LocalJudge?>.Fail(ex.Message); }
    }

    public async Task<Result> UpsertAsync(LocalJudge judge)
    {
        try { await _db.Connection.InsertOrReplaceAsync(judge); return Result.Ok(); }
        catch (Exception ex) { return Result.Fail(ex.Message); }
    }

    public async Task<Result> BulkUpsertAsync(IEnumerable<LocalJudge> judges)
    {
        try
        {
            foreach (var j in judges)
                await _db.Connection.InsertOrReplaceAsync(j);
            return Result.Ok();
        }
        catch (Exception ex) { return Result.Fail(ex.Message); }
    }

    public async Task<Result> DeleteAsync(int remoteId)
    {
        try
        {
            await _db.Connection.DeleteAsync<LocalJudge>(remoteId);
            return Result.Ok();
        }
        catch (Exception ex) { return Result.Fail(ex.Message); }
    }

    public async Task<Result> DeleteByEventAsync(int eventId)
    {
        try
        {
            await _db.Connection.ExecuteAsync("DELETE FROM local_judges WHERE EventId = ?", eventId);
            return Result.Ok();
        }
        catch (Exception ex) { return Result.Fail(ex.Message); }
    }

    public async Task<Result> DeleteAllAsync()
    {
        try
        {
            await _db.Connection.ExecuteAsync("DELETE FROM local_judges");
            return Result.Ok();
        }
        catch (Exception ex) { return Result.Fail(ex.Message); }
    }
}

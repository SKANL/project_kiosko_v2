using Nodus.Admin.Application.Interfaces.Persistence;
using Nodus.Admin.Domain.Common;
using Nodus.Admin.Domain.Entities;

namespace Nodus.Admin.Infrastructure.Persistence;

public sealed class JudgeRepository : IJudgeRepository
{
    private readonly NodusDatabase _db;
    public JudgeRepository(NodusDatabase db) => _db = db;

    public async Task<Result<IReadOnlyList<Judge>>> GetByEventAsync(int eventId)
    {
        try
        {
            var list = await _db.Connection.Table<Judge>()
                .Where(j => j.EventId == eventId)
                .ToListAsync();
            return Result<IReadOnlyList<Judge>>.Ok(list);
        }
        catch (Exception ex) { return Result<IReadOnlyList<Judge>>.Fail(ex.Message); }
    }

    public async Task<Result<Judge>> GetByIdAsync(int id)
    {
        try
        {
            var j = await _db.Connection.FindAsync<Judge>(id);
            return j is null ? Result<Judge>.Fail($"Judge {id} not found") : Result<Judge>.Ok(j);
        }
        catch (Exception ex) { return Result<Judge>.Fail(ex.Message); }
    }

    public async Task<Result<Judge?>> GetByPublicKeyAsync(string publicKeyBase64)
    {
        try
        {
            var j = await _db.Connection.Table<Judge>()
                .Where(x => x.PublicKeyBase64 == publicKeyBase64)
                .FirstOrDefaultAsync();
            return Result<Judge?>.Ok(j);
        }
        catch (Exception ex) { return Result<Judge?>.Fail(ex.Message); }
    }

    public async Task<Result<Judge?>> GetByNameAndEventAsync(string name, int eventId)
    {
        try
        {
            var j = await _db.Connection.Table<Judge>()
                .Where(x => x.EventId == eventId && x.Name == name)
                .FirstOrDefaultAsync();
            return Result<Judge?>.Ok(j);
        }
        catch (Exception ex) { return Result<Judge?>.Fail(ex.Message); }
    }

    public async Task<Result<int>> CreateAsync(Judge judge)
    {
        try
        {
            await _db.Connection.InsertAsync(judge);
            return Result<int>.Ok(judge.Id);
        }
        catch (Exception ex) { return Result<int>.Fail(ex.Message); }
    }

    public async Task<Result> UpdateAsync(Judge judge)
    {
        try { await _db.Connection.UpdateAsync(judge); return Result.Ok(); }
        catch (Exception ex) { return Result.Fail(ex.Message); }
    }

    public async Task<Result> DeleteAsync(int id)
    {
        try { await _db.Connection.DeleteAsync<Judge>(id); return Result.Ok(); }
        catch (Exception ex) { return Result.Fail(ex.Message); }
    }

    public async Task<Result> DeleteByEventAsync(int eventId)
    {
        try
        {
            await _db.Connection.ExecuteAsync("DELETE FROM Judge WHERE EventId = ?", eventId);
            return Result.Ok();
        }
        catch (Exception ex) { return Result.Fail(ex.Message); }
    }

    public async Task<Result> BulkInsertAsync(IEnumerable<Judge> judges)
    {
        try { await _db.Connection.InsertAllAsync(judges); return Result.Ok(); }
        catch (Exception ex) { return Result.Fail(ex.Message); }
    }
}

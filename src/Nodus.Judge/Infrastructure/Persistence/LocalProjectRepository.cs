using Nodus.Judge.Application.Interfaces.Persistence;
using Nodus.Judge.Domain.Common;
using Nodus.Judge.Domain.Entities;

namespace Nodus.Judge.Infrastructure.Persistence;

public sealed class LocalProjectRepository : ILocalProjectRepository
{
    private readonly NodusDatabase _db;
    public LocalProjectRepository(NodusDatabase db) => _db = db;

    public async Task<Result<IReadOnlyList<LocalProject>>> GetByEventAsync(int eventId)
    {
        try
        {
            var list = await _db.Connection.Table<LocalProject>()
                .Where(p => p.EventId == eventId)
                .OrderBy(p => p.SortOrder)
                .ToListAsync();
            return Result<IReadOnlyList<LocalProject>>.Ok(list);
        }
        catch (Exception ex) { return Result<IReadOnlyList<LocalProject>>.Fail(ex.Message); }
    }

    public async Task<Result<LocalProject>> GetByIdAsync(int remoteId)
    {
        try
        {
            var p = await _db.Connection.FindAsync<LocalProject>(remoteId);
            return p is null ? Result<LocalProject>.Fail($"Project {remoteId} not found") : Result<LocalProject>.Ok(p);
        }
        catch (Exception ex) { return Result<LocalProject>.Fail(ex.Message); }
    }

    public async Task<Result<LocalProject?>> GetByCodeAsync(int eventId, string projectCode)
    {
        try
        {
            var normalized = projectCode.Trim().ToUpperInvariant();
            var project = await _db.Connection.Table<LocalProject>()
                .Where(p => p.EventId == eventId && p.ProjectCode == normalized)
                .FirstOrDefaultAsync();
            return Result<LocalProject?>.Ok(project);
        }
        catch (Exception ex) { return Result<LocalProject?>.Fail(ex.Message); }
    }

    public async Task<Result<int>> GetMaxSequenceAsync(int eventId)
    {
        try
        {
            var project = await _db.Connection.Table<LocalProject>()
                .Where(p => p.EventId == eventId)
                .OrderByDescending(p => p.SequenceNumber)
                .FirstOrDefaultAsync();
            return Result<int>.Ok(project?.SequenceNumber ?? 0);
        }
        catch (Exception ex) { return Result<int>.Fail(ex.Message); }
    }

    public async Task<Result> BulkUpsertAsync(IEnumerable<LocalProject> projects)
    {
        try
        {
            foreach (var p in projects)
                await _db.Connection.InsertOrReplaceAsync(p);
            return Result.Ok();
        }
        catch (Exception ex) { return Result.Fail(ex.Message); }
    }

    public async Task<Result> DeleteByEventAsync(int eventId)
    {
        try
        {
            await _db.Connection.ExecuteAsync("DELETE FROM local_projects WHERE EventId = ?", eventId);
            return Result.Ok();
        }
        catch (Exception ex) { return Result.Fail(ex.Message); }
    }

    public async Task<Result> DeleteAllAsync()
    {
        try
        {
            await _db.Connection.ExecuteAsync("DELETE FROM local_projects");
            return Result.Ok();
        }
        catch (Exception ex) { return Result.Fail(ex.Message); }
    }
}

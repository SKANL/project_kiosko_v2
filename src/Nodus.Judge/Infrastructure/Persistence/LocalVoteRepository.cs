using Nodus.Judge.Application.Interfaces.Persistence;
using Nodus.Judge.Domain.Common;
using Nodus.Judge.Domain.Entities;
using Nodus.Judge.Domain.Enums;

namespace Nodus.Judge.Infrastructure.Persistence;

public sealed class LocalVoteRepository : ILocalVoteRepository
{
    private readonly NodusDatabase _db;
    public LocalVoteRepository(NodusDatabase db) => _db = db;

    public async Task<Result<IReadOnlyList<LocalVote>>> GetByEventAsync(int eventId)
    {
        try
        {
            var list = await _db.Connection.Table<LocalVote>().Where(v => v.EventId == eventId).ToListAsync();
            return Result<IReadOnlyList<LocalVote>>.Ok(list);
        }
        catch (Exception ex) { return Result<IReadOnlyList<LocalVote>>.Fail(ex.Message); }
    }

    public async Task<Result<IReadOnlyList<LocalVote>>> GetPendingSyncAsync()
    {
        try
        {
            var list = await _db.Connection.Table<LocalVote>()
                .Where(v => v.SyncStatus == SyncStatus.Pending).ToListAsync();
            return Result<IReadOnlyList<LocalVote>>.Ok(list);
        }
        catch (Exception ex) { return Result<IReadOnlyList<LocalVote>>.Fail(ex.Message); }
    }

    public async Task<Result<bool>> ExistsAsync(int eventId, int projectId, int judgeId)
    {
        try
        {
            int c = await _db.Connection.Table<LocalVote>()
                .Where(v => v.EventId == eventId && v.ProjectId == projectId && v.JudgeId == judgeId)
                .CountAsync();
            return Result<bool>.Ok(c > 0);
        }
        catch (Exception ex) { return Result<bool>.Fail(ex.Message); }
    }

    public async Task<Result<LocalVote?>> GetLatestByJudgeProjectAsync(int eventId, int projectId, int judgeId)
    {
        try
        {
            var vote = await _db.Connection.Table<LocalVote>()
                .Where(v => v.EventId == eventId && v.ProjectId == projectId && v.JudgeId == judgeId)
                .OrderByDescending(v => v.Version)
                .FirstOrDefaultAsync();
            return Result<LocalVote?>.Ok(vote);
        }
        catch (Exception ex) { return Result<LocalVote?>.Fail(ex.Message); }
    }

    public async Task<Result<int>> UpsertAsync(LocalVote vote)
    {
        try
        {
            if (vote.Id == 0) await _db.Connection.InsertAsync(vote);
            else              await _db.Connection.UpdateAsync(vote);
            return Result<int>.Ok(vote.Id);
        }
        catch (Exception ex) { return Result<int>.Fail(ex.Message); }
    }

    public async Task<Result> MarkSyncedAsync(IEnumerable<int> ids)
    {
        try
        {
            var idList = ids.ToList();
            if (idList.Count == 0) return Result.Ok();

            // Single UPDATE … WHERE Id IN (…) instead of N round-trips
            var placeholders = string.Join(",", idList.Select(_ => "?"));
            var args = new object[] { (int)SyncStatus.Synced }
                .Concat(idList.Cast<object>())
                .ToArray();
            await _db.Connection.ExecuteAsync(
                $"UPDATE local_votes SET SyncStatus = ? WHERE Id IN ({placeholders})",
                args);
            return Result.Ok();
        }
        catch (Exception ex) { return Result.Fail(ex.Message); }
    }

    public async Task<Result> DeleteAllAsync()
    {
        try
        {
            await _db.Connection.ExecuteAsync("DELETE FROM local_votes");
            return Result.Ok();
        }
        catch (Exception ex) { return Result.Fail(ex.Message); }
    }
}

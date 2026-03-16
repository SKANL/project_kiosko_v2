using Nodus.Admin.Application.Interfaces.Persistence;
using Nodus.Admin.Domain.Common;
using Nodus.Admin.Domain.Entities;

namespace Nodus.Admin.Infrastructure.Persistence;

public sealed class VoteRepository : IVoteRepository
{
    private readonly NodusDatabase _db;
    public VoteRepository(NodusDatabase db) => _db = db;

    public async Task<Result<IReadOnlyList<Vote>>> GetByEventAsync(int eventId)
    {
        try
        {
            var list = await _db.Connection.Table<Vote>().Where(v => v.EventId == eventId).ToListAsync();
            return Result<IReadOnlyList<Vote>>.Ok(list);
        }
        catch (Exception ex) { return Result<IReadOnlyList<Vote>>.Fail(ex.Message); }
    }

    public async Task<Result<IReadOnlyList<Vote>>> GetByProjectAsync(int eventId, int projectId)
    {
        try
        {
            var list = await _db.Connection.Table<Vote>()
                .Where(v => v.EventId == eventId && v.ProjectId == projectId).ToListAsync();
            return Result<IReadOnlyList<Vote>>.Ok(list);
        }
        catch (Exception ex) { return Result<IReadOnlyList<Vote>>.Fail(ex.Message); }
    }

    public async Task<Result<IReadOnlyList<Vote>>> GetByJudgeAsync(int eventId, int judgeId)
    {
        try
        {
            var list = await _db.Connection.Table<Vote>()
                .Where(v => v.EventId == eventId && v.JudgeId == judgeId).ToListAsync();
            return Result<IReadOnlyList<Vote>>.Ok(list);
        }
        catch (Exception ex) { return Result<IReadOnlyList<Vote>>.Fail(ex.Message); }
    }

    public async Task<Result<bool>> ExistsAsync(int eventId, int projectId, int judgeId)
    {
        try
        {
            int count = await _db.Connection.Table<Vote>()
                .Where(v => v.EventId == eventId && v.ProjectId == projectId && v.JudgeId == judgeId)
                .CountAsync();
            return Result<bool>.Ok(count > 0);
        }
        catch (Exception ex) { return Result<bool>.Fail(ex.Message); }
    }

    public async Task<Result<Vote?>> GetLatestByJudgeProjectAsync(int eventId, int projectId, int judgeId)
    {
        try
        {
            var vote = await _db.Connection.Table<Vote>()
                .Where(v => v.EventId == eventId && v.ProjectId == projectId && v.JudgeId == judgeId)
                .OrderByDescending(v => v.Version)
                .FirstOrDefaultAsync();
            return Result<Vote?>.Ok(vote);
        }
        catch (Exception ex) { return Result<Vote?>.Fail(ex.Message); }
    }

    public async Task<Result<int>> UpsertAsync(Vote vote)
    {
        try
        {
            if (vote.Id == 0)
                await _db.Connection.InsertAsync(vote);
            else
                await _db.Connection.UpdateAsync(vote);
            return Result<int>.Ok(vote.Id);
        }
        catch (Exception ex) { return Result<int>.Fail(ex.Message); }
    }

    public async Task<Result> DeleteByEventAsync(int eventId)
    {
        try
        {
            await _db.Connection.ExecuteAsync("DELETE FROM votes WHERE EventId = ?", eventId);
            return Result.Ok();
        }
        catch (Exception ex) { return Result.Fail(ex.Message); }
    }
}

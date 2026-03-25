using Nodus.Judge.Domain.Common;
using Nodus.Judge.Domain.Entities;

namespace Nodus.Judge.Application.Interfaces.Persistence;

public interface ILocalVoteRepository
{
    Task<Result<IReadOnlyList<LocalVote>>> GetByEventAsync(int eventId);
    Task<Result<IReadOnlyList<LocalVote>>> GetPendingSyncAsync();
    Task<Result<bool>>                    ExistsAsync(int eventId, int projectId, int judgeId);
    Task<Result<LocalVote?>>              GetLatestByJudgeProjectAsync(int eventId, int projectId, int judgeId);
    Task<Result<int>>                     UpsertAsync(LocalVote vote);
    Task<Result>                          MarkSyncedAsync(IEnumerable<int> ids);
    Task<Result>                          DeleteAllAsync();

    // Get a vote by its local Id
    Task<Result<LocalVote?>>              GetByIdAsync(int id);
}

using Nodus.Admin.Domain.Common;
using Nodus.Admin.Domain.Entities;

namespace Nodus.Admin.Application.Interfaces.Persistence;

public interface IVoteRepository
{
    Task<Result<IReadOnlyList<Vote>>> GetByEventAsync(int eventId);
    Task<Result<IReadOnlyList<Vote>>> GetByProjectAsync(int eventId, int projectId);
    Task<Result<IReadOnlyList<Vote>>> GetByJudgeAsync(int eventId, int judgeId);
    Task<Result<bool>>                ExistsAsync(int eventId, int projectId, int judgeId);
    Task<Result<Vote?>>               GetLatestByJudgeProjectAsync(int eventId, int projectId, int judgeId);
    Task<Result<int>>                 UpsertAsync(Vote vote);
    Task<Result>                      DeleteByEventAsync(int eventId);
}

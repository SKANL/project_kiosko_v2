using Nodus.Judge.Domain.Common;
using Nodus.Judge.Domain.Entities;

namespace Nodus.Judge.Application.Interfaces.Persistence;

public interface ILocalJudgeRepository
{
    Task<Result<LocalJudge?>>              GetSelfAsync();
    Task<Result<IReadOnlyList<LocalJudge>>> GetByEventAsync(int eventId);
    Task<Result<LocalJudge?>>              GetByPublicKeyAsync(string publicKeyBase64);
    Task<Result>                           UpsertAsync(LocalJudge judge);
    Task<Result>                           BulkUpsertAsync(IEnumerable<LocalJudge> judges);
    Task<Result>                           DeleteAsync(int remoteId);
    Task<Result>                           DeleteByEventAsync(int eventId);
    Task<Result>                           DeleteAllAsync();
}

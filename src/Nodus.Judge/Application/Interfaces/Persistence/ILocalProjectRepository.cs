using Nodus.Judge.Domain.Common;
using Nodus.Judge.Domain.Entities;

namespace Nodus.Judge.Application.Interfaces.Persistence;

public interface ILocalProjectRepository
{
    Task<Result<IReadOnlyList<LocalProject>>> GetByEventAsync(int eventId);
    Task<Result<LocalProject>>               GetByIdAsync(int remoteId);
    Task<Result<LocalProject?>>              GetByCodeAsync(int eventId, string projectCode);
    Task<Result<int>>                        GetMaxSequenceAsync(int eventId);
    Task<Result>                             BulkUpsertAsync(IEnumerable<LocalProject> projects);
    Task<Result>                             DeleteByEventAsync(int eventId);
    Task<Result>                             DeleteAllAsync();
}

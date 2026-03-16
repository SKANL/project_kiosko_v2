using Nodus.Judge.Domain.Common;
using Nodus.Judge.Domain.Entities;

namespace Nodus.Judge.Application.Interfaces.Persistence;

public interface ILocalEventRepository
{
    Task<Result<LocalEvent?>> GetActiveAsync();
    Task<Result<LocalEvent>>  GetByIdAsync(int remoteId);
    Task<Result>              UpsertAsync(LocalEvent evt);
    Task<Result>              DeleteAllAsync();
}

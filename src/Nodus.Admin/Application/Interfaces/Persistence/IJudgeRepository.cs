using Nodus.Admin.Domain.Common;
using Nodus.Admin.Domain.Entities;

namespace Nodus.Admin.Application.Interfaces.Persistence;

public interface IJudgeRepository
{
    Task<Result<IReadOnlyList<Judge>>> GetByEventAsync(int eventId);
    Task<Result<Judge>>               GetByIdAsync(int id);
    Task<Result<Judge?>>              GetByPublicKeyAsync(string publicKeyBase64);
    Task<Result<Judge?>>              GetByNameAndEventAsync(string name, int eventId);
    Task<Result<int>>                 CreateAsync(Judge judge);
    Task<Result>                      UpdateAsync(Judge judge);
    Task<Result>                      DeleteAsync(int id);
    Task<Result>                      DeleteByEventAsync(int eventId);
    Task<Result>                      BulkInsertAsync(IEnumerable<Judge> judges);
}

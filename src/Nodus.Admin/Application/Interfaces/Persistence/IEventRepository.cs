using Nodus.Admin.Domain.Common;
using Nodus.Admin.Domain.Entities;

namespace Nodus.Admin.Application.Interfaces.Persistence;

public interface IEventRepository
{
    Task<Result<NodusEvent?>> GetCurrentAsync();
    Task<Result<IReadOnlyList<NodusEvent>>> GetAllAsync();
    Task<Result<NodusEvent>>  GetByIdAsync(int id);
    Task<Result<int>>         CreateAsync(NodusEvent evt);
    Task<Result>              UpdateAsync(NodusEvent evt);
    Task<Result>              DeleteAsync(int id);
}

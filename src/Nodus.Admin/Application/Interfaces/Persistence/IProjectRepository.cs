using Nodus.Admin.Domain.Common;
using Nodus.Admin.Domain.Entities;

namespace Nodus.Admin.Application.Interfaces.Persistence;

public interface IProjectRepository
{
    Task<Result<IReadOnlyList<Project>>> GetByEventAsync(int eventId);
    Task<Result<IReadOnlyList<Project>>> GetByEventSinceSequenceAsync(int eventId, int sinceSequence);
    Task<Result<Project>>               GetByIdAsync(int id);
    Task<Result<int>>                   CreateAsync(Project project);
    Task<Result>                        UpdateAsync(Project project);
    Task<Result>                        DeleteAsync(int id);
    Task<Result>                        DeleteByEventAsync(int eventId);
    Task<Result>                        BulkInsertAsync(IEnumerable<Project> projects);
}

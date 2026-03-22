using Nodus.Admin.Application.Interfaces.Persistence;
using Nodus.Admin.Domain.Common;
using Nodus.Admin.Domain.Entities;

namespace Nodus.Admin.Infrastructure.Persistence;

public sealed class ProjectRepository : IProjectRepository
{
    private readonly NodusDatabase _db;
    public ProjectRepository(NodusDatabase db) => _db = db;

    public async Task<Result<IReadOnlyList<Project>>> GetByEventAsync(int eventId)
    {
        try
        {
            var list = await _db.Connection.Table<Project>()
                .Where(p => p.EventId == eventId)
                .OrderBy(p => p.SortOrder)
                .ToListAsync();
            return Result<IReadOnlyList<Project>>.Ok(list);
        }
        catch (Exception ex) { return Result<IReadOnlyList<Project>>.Fail(ex.Message); }
    }

    public async Task<Result<Project>> GetByIdAsync(int id)
    {
        try
        {
            var p = await _db.Connection.FindAsync<Project>(id);
            return p is null ? Result<Project>.Fail($"Project {id} not found") : Result<Project>.Ok(p);
        }
        catch (Exception ex) { return Result<Project>.Fail(ex.Message); }
    }

    public async Task<Result<IReadOnlyList<Project>>> GetByEventSinceSequenceAsync(int eventId, int sinceSequence)
    {
        try
        {
            var list = await _db.Connection.Table<Project>()
                .Where(p => p.EventId == eventId && p.SequenceNumber > sinceSequence)
                .OrderBy(p => p.SequenceNumber)
                .ToListAsync();
            return Result<IReadOnlyList<Project>>.Ok(list);
        }
        catch (Exception ex) { return Result<IReadOnlyList<Project>>.Fail(ex.Message); }
    }

    public async Task<Result<int>> CreateAsync(Project project)
    {
        try
        {
            if (project.SequenceNumber <= 0 && project.EventId > 0)
            {
                var maxSequence = await _db.Connection.Table<Project>()
                    .Where(item => item.EventId == project.EventId)
                    .OrderByDescending(item => item.SequenceNumber)
                    .FirstOrDefaultAsync()
                    .ConfigureAwait(false);
                project.SequenceNumber = (maxSequence?.SequenceNumber ?? 0) + 1;
            }

            await _db.Connection.InsertAsync(project).ConfigureAwait(false);
            return Result<int>.Ok(project.Id);
        }
        catch (Exception ex) { return Result<int>.Fail(ex.Message); }
    }

    public async Task<Result> UpdateAsync(Project project)
    {
        try 
        { 
            var maxSequence = await _db.Connection.Table<Project>()
                .Where(item => item.EventId == project.EventId)
                .OrderByDescending(item => item.SequenceNumber)
                .FirstOrDefaultAsync()
                .ConfigureAwait(false);
            
            project.SequenceNumber = (maxSequence?.SequenceNumber ?? 0) + 1;

            await _db.Connection.UpdateAsync(project).ConfigureAwait(false); 
            return Result.Ok(); 
        }
        catch (Exception ex) { return Result.Fail(ex.Message); }
    }

    public async Task<Result> DeleteAsync(int id)
    {
        try { await _db.Connection.DeleteAsync<Project>(id).ConfigureAwait(false); return Result.Ok(); }
        catch (Exception ex) { return Result.Fail(ex.Message); }
    }

    public async Task<Result> DeleteByEventAsync(int eventId)
    {
        try
        {
            await _db.Connection.ExecuteAsync("DELETE FROM Project WHERE EventId = ?", eventId)
                .ConfigureAwait(false);
            return Result.Ok();
        }
        catch (Exception ex) { return Result.Fail(ex.Message); }
    }

    public async Task<Result> BulkInsertAsync(IEnumerable<Project> projects)
    {
        try
        {
            int currentMax = -1;

            foreach (var project in projects)
            {
                if (project.SequenceNumber <= 0 && project.EventId > 0)
                {
                    if (currentMax == -1)
                    {
                        var maxSequence = await _db.Connection.Table<Project>()
                            .Where(item => item.EventId == project.EventId)
                            .OrderByDescending(item => item.SequenceNumber)
                            .FirstOrDefaultAsync()
                            .ConfigureAwait(false);
                        currentMax = maxSequence?.SequenceNumber ?? 0;
                    }
                    
                    project.SequenceNumber = ++currentMax;
                }
            }

            await _db.Connection.InsertAllAsync(projects).ConfigureAwait(false);
            return Result.Ok();
        }
        catch (Exception ex) { return Result.Fail(ex.Message); }
    }

    public async Task<string> GenerateUniqueCodeAsync(int eventId)
    {
        var count = await _db.Connection.Table<Project>()
            .Where(p => p.EventId == eventId)
            .CountAsync();
        return $"PROJ-{(count + 1):D3}";
    }
}

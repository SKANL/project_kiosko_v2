using Nodus.Admin.Application.Interfaces.Persistence;
using Nodus.Admin.Domain.Common;
using Nodus.Admin.Domain.Entities;
using Nodus.Admin.Domain.Enums;

namespace Nodus.Admin.Infrastructure.Persistence;

public sealed class EventRepository : IEventRepository
{
    private readonly NodusDatabase _db;
    public EventRepository(NodusDatabase db) => _db = db;

    public async Task<Result<NodusEvent?>> GetCurrentAsync()
    {
        try
        {
            // Prioritize Active event
            var active = await _db.Connection
                .Table<NodusEvent>()
                .Where(e => e.Status == EventStatus.Active)
                .FirstOrDefaultAsync();
            
            if (active is not null)
                return Result<NodusEvent?>.Ok(active);

            // Fallback to most recent Draft
            var draft = await _db.Connection
                .Table<NodusEvent>()
                .Where(e => e.Status == EventStatus.Draft)
                .OrderByDescending(e => e.Id)
                .FirstOrDefaultAsync();
            
            return Result<NodusEvent?>.Ok(draft);
        }
        catch (Exception ex) { return Result<NodusEvent?>.Fail(ex.Message); }
    }

    public async Task<Result<IReadOnlyList<NodusEvent>>> GetAllAsync()
    {
        try
        {
            var list = await _db.Connection.Table<NodusEvent>().ToListAsync();
            return Result<IReadOnlyList<NodusEvent>>.Ok(list);
        }
        catch (Exception ex) { return Result<IReadOnlyList<NodusEvent>>.Fail(ex.Message); }
    }

    public async Task<Result<NodusEvent>> GetByIdAsync(int id)
    {
        try
        {
            var evt = await _db.Connection.FindAsync<NodusEvent>(id);
            return evt is null
                ? Result<NodusEvent>.Fail($"Event {id} not found")
                : Result<NodusEvent>.Ok(evt);
        }
        catch (Exception ex) { return Result<NodusEvent>.Fail(ex.Message); }
    }

    public async Task<Result<int>> CreateAsync(NodusEvent evt)
    {
        try
        {
            await _db.Connection.InsertAsync(evt);
            return Result<int>.Ok(evt.Id);
        }
        catch (Exception ex) { return Result<int>.Fail(ex.Message); }
    }

    public async Task<Result> UpdateAsync(NodusEvent evt)
    {
        try
        {
            var current = await _db.Connection.FindAsync<NodusEvent>(evt.Id);
            if (current is null)
                return Result.Fail($"Event {evt.Id} not found");

            var rubricChanged = !string.Equals(current.RubricJson, evt.RubricJson, StringComparison.Ordinal);
            if (rubricChanged)
            {
                var hasVotes = await _db.Connection.Table<Vote>().Where(v => v.EventId == evt.Id).CountAsync() > 0;
                if (hasVotes)
                    return Result.Fail("La rúbrica no se puede modificar después de iniciar la votación.");

                evt.RubricVersion = Math.Max(current.RubricVersion, 1) + 1;
            }
            else
            {
                evt.RubricVersion = Math.Max(current.RubricVersion, 1);
            }

            evt.UpdatedAt = DateTime.UtcNow.ToString("O");
            await _db.Connection.UpdateAsync(evt);
            return Result.Ok();
        }
        catch (Exception ex) { return Result.Fail(ex.Message); }
    }

    public async Task<Result> DeleteAsync(int id)
    {
        try
        {
            await _db.Connection.DeleteAsync<NodusEvent>(id);
            return Result.Ok();
        }
        catch (Exception ex) { return Result.Fail(ex.Message); }
    }
}

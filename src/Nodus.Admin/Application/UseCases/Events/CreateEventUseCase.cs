using Nodus.Admin.Application.Interfaces.Persistence;
using Nodus.Admin.Application.Interfaces.Services;
using Nodus.Admin.Domain.Common;
using Nodus.Admin.Domain.Entities;
using Nodus.Admin.Domain.Enums;
using System.Security.Cryptography;
using System.Text.Json;

namespace Nodus.Admin.Application.UseCases.Events;

/// <summary>Creates a new event, generates its Ed25519 keypair, persists it, and builds the GATT bootstrap payload.</summary>
public sealed class CreateEventUseCase
{
    private readonly IEventRepository           _events;
    private readonly ICryptoService             _crypto;
    private readonly BuildBootstrapPayloadUseCase _bootstrap;

    public CreateEventUseCase(
        IEventRepository           events,
        ICryptoService             crypto,
        BuildBootstrapPayloadUseCase bootstrap)
    {
        _events    = events;
        _crypto    = crypto;
        _bootstrap = bootstrap;
    }

    /// <param name="RubricJson">
    /// Optional JSON array of scoring criteria. Empty string uses the 5-criterion default.
    /// Format: [{"id":"...","label":"...","weight":1.0,"min":0,"max":10,"step":0.5},...]
    /// </param>
    /// <param name="AccessPassword">Shared event password used to decrypt the Judge Access QR.</param>
    public sealed record Request(string Name, string Institution, string Description, string Date, string RubricJson = "", string AccessPassword = "");

    public async Task<Result<NodusEvent>> ExecuteAsync(Request req)
    {
        if (string.IsNullOrWhiteSpace(req.Name))
            return Result<NodusEvent>.Fail("Event name is required");
        if (string.IsNullOrWhiteSpace(req.AccessPassword) || req.AccessPassword.Trim().Length < 6)
            return Result<NodusEvent>.Fail("Event password must be at least 6 characters");

        var (pub, priv) = _crypto.GenerateKeyPair();

        var evt = new NodusEvent
        {
            Name          = req.Name.Trim(),
            Institution   = req.Institution.Trim(),
            Description   = req.Description.Trim(),
            Date          = req.Date,
            Status        = EventStatus.Draft,
            RubricVersion = 1,
            RubricJson    = req.RubricJson.Trim(),
            SharedKeyBase64          = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)),
            PublicKeyBase64          = pub,
            EncryptedPrivateKeyBase64 = priv,   // Stored plain for now — encrypted when PIN set
            CreatedAt     = DateTime.UtcNow.ToString("O"),
            UpdatedAt     = DateTime.UtcNow.ToString("O")
        };

        var createResult = await _events.CreateAsync(evt);
        if (createResult.IsFail)
            return Result<NodusEvent>.Fail(createResult.Error!);

        evt.Id = createResult.Value!;

        var accessPayload = JsonSerializer.Serialize(new JudgeAccessPayload(evt.Id, evt.Name, evt.SharedKeyBase64));
        var accessResult = _crypto.EncryptPayloadWithPassword(
            accessPayload,
            req.AccessPassword.Trim(),
            $"event:{evt.Id}:access");
        if (accessResult.IsFail)
        {
            await _events.DeleteAsync(evt.Id);
            return Result<NodusEvent>.Fail(accessResult.Error!);
        }

        evt.AccessQrPayload = $"nodus://join?eid={evt.Id}&token={Uri.EscapeDataString(accessResult.Value!)}";
        var updateResult = await _events.UpdateAsync(evt);
        if (updateResult.IsFail)
        {
            await _events.DeleteAsync(evt.Id);
            return Result<NodusEvent>.Fail(updateResult.Error!);
        }

        // Rebuild bootstrap payload (event has no projects/judges yet — still useful to update)
        await _bootstrap.ExecuteAsync(evt.Id);

        return Result<NodusEvent>.Ok(evt);
    }

    private sealed record JudgeAccessPayload(int EventId, string EventName, string SharedKeyBase64);
}

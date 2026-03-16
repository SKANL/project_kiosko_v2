using System.IO.Compression;
using System.Reactive.Linq;
using System.Text;
using System.Text.Json;
using Nodus.Judge.Application.DTOs;
using Nodus.Judge.Application.Interfaces.Persistence;
using Nodus.Judge.Application.Interfaces.Services;
using Nodus.Judge.Domain.Common;
using Nodus.Judge.Domain.Entities;
using Nodus.Judge.Infrastructure.Ble;

namespace Nodus.Judge.Application.UseCases.Onboarding;

/// <summary>
/// Full sync from Admin (Decision #56 flow):
///   1. Write sync_request (0x06) to NODUS_DATA_WRITE
///   2. Read NODUS_BOOTSTRAP_READ (byte prefix 0x03)
///   3. Deserialize BootstrapPayloadDto
///   4. Persist LocalEvent, LocalProjects, LocalJudges
/// </summary>
public sealed class SyncFromAdminUseCase
{
    private readonly IBleGattClientService  _client;
    private readonly ILocalEventRepository  _events;
    private readonly ILocalProjectRepository _projects;
    private readonly ILocalJudgeRepository  _judges;
    private readonly IAppSettingsService    _settings;
    private readonly ICryptoService         _crypto;
    private static readonly TimeSpan MinPollInterval = TimeSpan.FromSeconds(50);
    private static readonly TimeSpan MaxPollInterval = TimeSpan.FromSeconds(70);
    private readonly Random _random = new();
    private CancellationTokenSource? _pollingCts;

    public SyncFromAdminUseCase(
        IBleGattClientService   client,
        ILocalEventRepository   events,
        ILocalProjectRepository projects,
        ILocalJudgeRepository   judges,
        IAppSettingsService     settings,
        ICryptoService          crypto)
    {
        _client   = client;
        _events   = events;
        _projects = projects;
        _judges   = judges;
        _settings = settings;
        _crypto   = crypto;
    }

    public sealed record RegistrationContext(int EventId, string JudgeName, string SharedKeyBase64);

    public async Task<Result> EnsureAdminConnectionAsync(int timeoutSeconds = 15)
        => await _client.EnsureConnectedAsync(timeoutSeconds);

    public async Task<Result<BootstrapPayloadDto>> ExecuteAsync(RegistrationContext? registration = null)
    {
        // If not already connected, scan for the Admin device and connect automatically.
        var connectResult = await _client.EnsureConnectedAsync(timeoutSeconds: 20);
        if (connectResult.IsFail)
            return Result<BootstrapPayloadDto>.Fail(connectResult.Error!);

        byte[]? preReadPayload = await BuildPreReadPayloadAsync(registration);
        if (registration is not null && preReadPayload is null)
            return Result<BootstrapPayloadDto>.Fail("Judge identity is not ready for registration");

        Result<BootstrapPayloadDto> parseResult = Result<BootstrapPayloadDto>.Fail("Bootstrap sync did not start");
        for (int attempt = 0; attempt < 4; attempt++)
        {
            var rawResult = await TrySyncWithReconnectAsync(preReadPayload, 20);
            if (rawResult.IsFail)
            {
                parseResult = Result<BootstrapPayloadDto>.Fail(rawResult.Error!);
                continue;
            }

            parseResult = TryParseBootstrapPayload(rawResult.Value!);
            if (parseResult.IsOk || !LooksLikeIncompletePayload(parseResult.Error))
                break;
        }

        if (parseResult.IsFail)
            return Result<BootstrapPayloadDto>.Fail(parseResult.Error!);

        var dto = parseResult.Value!;

        // Load the self-judge now, before overwriting judge rows, so we can resolve its real RemoteId.
        var selfResult = await _judges.GetSelfAsync();
        string? selfPublicKey = selfResult.IsOk ? selfResult.Value?.PublicKeyBase64 : null;

        // Persist event
        var localEvt = new LocalEvent
        {
            RemoteId              = dto.EventId,
            Name                  = dto.EventName,
            Institution           = dto.Institution,
            Description           = string.Empty,
            Date                  = dto.EventDate,
            AdminPublicKeyBase64  = dto.AdminPublicKeyBase64,
            RubricVersion         = Math.Max(dto.RubricVersion, 1),
            RubricJson            = dto.RubricJson ?? string.Empty,
            FinishedAt            = dto.FinishedAt ?? string.Empty,
            GraceEndsAt           = dto.GraceEndsAt ?? string.Empty,
            SyncedAt              = DateTime.UtcNow.ToString("O")
        };
        var evtResult = await _events.UpsertAsync(localEvt);
        if (evtResult.IsFail) return Result<BootstrapPayloadDto>.Fail(evtResult.Error!);

        // Persist projects
        var localProjects = dto.Projects.Select(p => new LocalProject
        {
            RemoteId    = p.Id,
            EventId     = dto.EventId,
            Name        = p.Name,
            Description = p.Description,
            Category    = p.Category,
            TeamMembers = p.TeamMembers,
            SortOrder   = p.SortOrder,
            ProjectCode = p.ProjectCode ?? string.Empty,
            StandNumber = p.StandNumber ?? string.Empty,
            GithubLink = p.GithubLink ?? string.Empty,
            SequenceNumber = p.SequenceNumber,
            SyncedAt    = DateTime.UtcNow.ToString("O")
        });
        var projResult = await _projects.BulkUpsertAsync(localProjects);
        if (projResult.IsFail) return Result<BootstrapPayloadDto>.Fail(projResult.Error!);

        // Persist judges
        var localJudges = dto.Judges.Select(j => new LocalJudge
        {
            RemoteId         = j.Id,
            EventId          = dto.EventId,
            Name             = j.Name,
            PublicKeyBase64  = j.PublicKeyBase64,
            IsSelf           = false,
            SyncedAt         = DateTime.UtcNow.ToString("O")
        });
        var judgeResult = await _judges.BulkUpsertAsync(localJudges);
        if (judgeResult.IsFail) return Result<BootstrapPayloadDto>.Fail(judgeResult.Error!);

        // Resolve the self-judge's real Admin-assigned ID.
        // The payload includes all judges; find the one whose public key matches this device's key.
        // Without this, self.RemoteId stays 0 and every vote payload is rejected by the Admin
        // (ProcessVoteUseCase validates JudgeId > 0).
        if (!string.IsNullOrEmpty(selfPublicKey))
        {
            var matched = dto.Judges.FirstOrDefault(j => j.PublicKeyBase64 == selfPublicKey);
            if (matched is not null)
            {
                // Delete the placeholder self-judge row (RemoteId = 0)
                if (selfResult.Value is not null)
                    await _judges.DeleteAsync(selfResult.Value.RemoteId);

                // Mark the already-inserted judge row for this device as IsSelf = true
                var updatedSelf = new LocalJudge
                {
                    RemoteId                  = matched.Id,
                    EventId                   = dto.EventId,
                    Name                      = matched.Name,
                    PublicKeyBase64           = matched.PublicKeyBase64,
                    EncryptedPrivateKeyBase64 = selfResult.Value?.EncryptedPrivateKeyBase64 ?? string.Empty,
                    IsSelf                    = true,
                    SyncedAt                  = DateTime.UtcNow.ToString("O")
                };
                await _judges.UpsertAsync(updatedSelf);

                _settings.SelfJudgeId = matched.Id;
            }
        }

        _settings.ActiveEventId = dto.EventId;

        // Persist the per-event shared key so delta polls can decrypt AES-GCM envelopes
        // even after the app is restarted (the RegistrationContext is gone by then).
        if (registration is not null && !string.IsNullOrWhiteSpace(registration.SharedKeyBase64))
            _settings.SharedEventKey = registration.SharedKeyBase64;

        _settings.Save();

        StartAutoPolling();

        return Result<BootstrapPayloadDto>.Ok(dto);
    }

    public void StartAutoPolling()
    {
        _pollingCts?.Cancel();

        var eventId = _settings.ActiveEventId;
        var judgeId = _settings.SelfJudgeId;
        if (!eventId.HasValue || !judgeId.HasValue || eventId.Value <= 0 || judgeId.Value <= 0)
            return;

        _pollingCts = new CancellationTokenSource();
        _ = Task.Run(() => PollLoopAsync(_pollingCts.Token));
    }

    public void StopAutoPolling()
    {
        _pollingCts?.Cancel();
        _pollingCts = null;
    }

    private async Task PollLoopAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                var eventId = _settings.ActiveEventId;
                if (!eventId.HasValue || eventId.Value <= 0)
                    return;

                var eventResult = await _events.GetByIdAsync(eventId.Value);
                if (eventResult.IsOk && eventResult.Value is not null && IsPollingClosed(eventResult.Value))
                    return;

                var preReadPayload = await BuildPreReadPayloadAsync(null);
                if (preReadPayload is not null)
                {
                    var connectResult = await _client.EnsureConnectedAsync(timeoutSeconds: 12);
                    if (connectResult.IsFail)
                        continue;

                    var syncResult = await TrySyncWithReconnectAsync(preReadPayload, 12);
                    if (syncResult.IsOk)
                        await MergeSyncPayloadAsync(syncResult.Value!);
                }
            }
            catch
            {
            }

            try
            {
                var delay = MinPollInterval + TimeSpan.FromMilliseconds(_random.NextDouble() * (MaxPollInterval - MinPollInterval).TotalMilliseconds);
                await Task.Delay(delay, cancellationToken);
            }
            catch (TaskCanceledException)
            {
                return;
            }
        }
    }

    private async Task MergeSyncPayloadAsync(byte[] raw)
    {
        var parse = TryParseBootstrapPayload(raw);
        if (parse.IsFail)
            return;
        var dto = parse.Value!;

        var existingEvent = await _events.GetByIdAsync(dto.EventId);
        if (existingEvent.IsOk && existingEvent.Value is not null)
        {
            existingEvent.Value.Name = dto.EventName;
            existingEvent.Value.Institution = dto.Institution;
            existingEvent.Value.Date = dto.EventDate;
            existingEvent.Value.AdminPublicKeyBase64 = dto.AdminPublicKeyBase64;
            existingEvent.Value.RubricJson = dto.RubricJson ?? existingEvent.Value.RubricJson;
            existingEvent.Value.FinishedAt = dto.FinishedAt ?? string.Empty;
            existingEvent.Value.GraceEndsAt = dto.GraceEndsAt ?? string.Empty;
            existingEvent.Value.RubricVersion = Math.Max(dto.RubricVersion, existingEvent.Value.RubricVersion);
            existingEvent.Value.SyncedAt = DateTime.UtcNow.ToString("O");
            await _events.UpsertAsync(existingEvent.Value);
        }

        if (dto.Projects.Count > 0)
        {
            await _projects.BulkUpsertAsync(dto.Projects.Select(project => new LocalProject
            {
                RemoteId = project.Id,
                EventId = dto.EventId,
                Name = project.Name,
                Description = project.Description,
                Category = project.Category,
                TeamMembers = project.TeamMembers,
                SortOrder = project.SortOrder,
                ProjectCode = project.ProjectCode ?? string.Empty,
                StandNumber = project.StandNumber ?? string.Empty,
                GithubLink = project.GithubLink ?? string.Empty,
                SequenceNumber = project.SequenceNumber,
                SyncedAt = DateTime.UtcNow.ToString("O")
            }));
        }
    }

    private async Task<byte[]?> BuildPreReadPayloadAsync(RegistrationContext? registration)
    {
        if (registration is not null)
        {
            var selfResult = await _judges.GetSelfAsync();
            if (!selfResult.IsOk || selfResult.Value is null)
                return null;

            var body = JsonSerializer.Serialize(new JudgeRegisterPayload(
                registration.JudgeName.Trim(),
                selfResult.Value.PublicKeyBase64));
            var cipher = _crypto.EncryptPayloadWithSharedKey(body, registration.SharedKeyBase64);
            if (cipher.IsFail)
                return null;

            return BuildPrefixedJson(NodusPrefix.JudgeRegister, new JudgeRegisterEnvelope(registration.EventId, cipher.Value!));
        }

        var judgeId = _settings.SelfJudgeId ?? 0;
        var eventId = _settings.ActiveEventId ?? 0;
        if (judgeId <= 0 || eventId <= 0)
            return null;

        var sinceSeqResult = await _projects.GetMaxSequenceAsync(eventId);
        var sinceSeq = sinceSeqResult.IsOk ? sinceSeqResult.Value : 0;

        return BuildPrefixedJson(NodusPrefix.SyncRequest, new SyncRequestDto(eventId, judgeId, sinceSeq));
    }

    private static byte[] BuildPrefixedJson<T>(byte prefix, T payload)
    {
        byte[] json = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(payload));
        byte[] raw = new byte[json.Length + 1];
        raw[0] = prefix;
        json.CopyTo(raw, 1);
        return raw;
    }

    private sealed record JudgeRegisterEnvelope(int EventId, string CipherTextBase64);
    private sealed record JudgeRegisterPayload(string JudgeName, string PublicKeyBase64);

    private Result<BootstrapPayloadDto> TryParseBootstrapPayload(byte[] raw)
    {
        if (raw.Length < 2 || (raw[0] != NodusPrefix.Bootstrap && raw[0] != NodusPrefix.ProjectSync))
            return Result<BootstrapPayloadDto>.Fail(raw.Length == 0
                ? "Empty bootstrap payload"
                : $"Unexpected prefix byte: 0x{raw[0]:X2}");

        try
        {
            byte[] body = raw[1..];

            // ── Envelope Version discriminator ────────────────────────────────────
            // v2 = 0x02 → AES-256-GCM encrypted (Decision #44)
            // v1 = 0x01 → GZip compressed, unencrypted (legacy)
            byte[] compressedBytes;
            int expectedJsonLength = -1;

            if (body.Length >= 13 && body[0] == 0x02)
            {
                // v2 envelope: [0x02][jsonLen:4][compLen:4][aesLen:4][nonce(12)|tag(16)|cipher...]
                expectedJsonLength        = BitConverter.ToInt32(body, 1);
                int expectedCompressedLen = BitConverter.ToInt32(body, 5);
                int aesLen                = BitConverter.ToInt32(body, 9);

                if (expectedJsonLength <= 0 || expectedCompressedLen <= 0 || aesLen <= 0)
                    return Result<BootstrapPayloadDto>.Fail("Invalid v2 bootstrap envelope lengths");

                if (body.Length < 13 + aesLen)
                    return Result<BootstrapPayloadDto>.Fail(
                        $"Incomplete v2 bootstrap envelope: expected {aesLen} AES bytes but got {Math.Max(0, body.Length - 13)}");

                byte[] aesPacket = body.AsSpan(13, aesLen).ToArray();
                string sharedKey = _settings.SharedEventKey;
                if (string.IsNullOrWhiteSpace(sharedKey))
                    return Result<BootstrapPayloadDto>.Fail("No shared event key stored — cannot decrypt bootstrap payload.");

                byte[]? decrypted = DecryptAesGcm(aesPacket, sharedKey);
                if (decrypted is null)
                    return Result<BootstrapPayloadDto>.Fail("AES-GCM decryption of bootstrap payload failed.");

                compressedBytes = decrypted;

                if (compressedBytes.Length != expectedCompressedLen)
                    return Result<BootstrapPayloadDto>.Fail(
                        $"Decompressed length mismatch: expected {expectedCompressedLen} got {compressedBytes.Length}");
            }
            else if (body.Length >= 9 && body[0] == 0x01)
            {
                // v1 envelope: [0x01][jsonLen:4][compLen:4][compressedBytes...]
                expectedJsonLength = BitConverter.ToInt32(body, 1);
                int expectedCompressedLength = BitConverter.ToInt32(body, 5);

                if (expectedJsonLength <= 0 || expectedCompressedLength <= 0)
                    return Result<BootstrapPayloadDto>.Fail("Invalid bootstrap envelope lengths");

                if (body.Length < 9 + expectedCompressedLength)
                    return Result<BootstrapPayloadDto>.Fail(
                        $"Incomplete bootstrap envelope: expected {expectedCompressedLength} compressed bytes but got {Math.Max(0, body.Length - 9)}");

                compressedBytes = body.AsSpan(9, expectedCompressedLength).ToArray();
            }
            else
            {
                // Legacy payload: [prefix][compressedOrJsonBytes...]
                compressedBytes = body;
            }

            // ── Decompress ────────────────────────────────────────────────────────
            byte[] jsonBytes;
            var looksGzip = compressedBytes.Length > 2 && compressedBytes[0] == 0x1F && compressedBytes[1] == 0x8B;
            if (looksGzip)
            {
                using var src = new MemoryStream(compressedBytes);
                using var gz  = new GZipStream(src, CompressionMode.Decompress);
                using var dst = new MemoryStream();
                gz.CopyTo(dst);
                jsonBytes = dst.ToArray();
            }
            else
            {
                jsonBytes = compressedBytes;
            }

            if (expectedJsonLength > 0 && jsonBytes.Length != expectedJsonLength)
                return Result<BootstrapPayloadDto>.Fail(
                    $"Incomplete decompressed bootstrap: expected {expectedJsonLength} bytes but got {jsonBytes.Length}");

            var dto = JsonSerializer.Deserialize<BootstrapPayloadDto>(Encoding.UTF8.GetString(jsonBytes),
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            return dto is null
                ? Result<BootstrapPayloadDto>.Fail("Deserialize returned null")
                : Result<BootstrapPayloadDto>.Ok(dto);
        }
        catch (Exception ex)
        {
            return Result<BootstrapPayloadDto>.Fail($"Deserialize failed: {ex.Message}");
        }
    }

    /// <summary>
    /// AES-256-GCM decrypt a packet built by the Admin: [nonce(12)][tag(16)][cipher...].
    /// Returns the plaintext bytes, or null on failure.
    /// </summary>
    private static byte[]? DecryptAesGcm(byte[] packet, string sharedKeyBase64)
    {
        try
        {
            const int nonceLen = 12;
            const int tagLen   = 16;
            if (packet.Length <= nonceLen + tagLen)
                return null;

            byte[] key    = Convert.FromBase64String(sharedKeyBase64);
            byte[] nonce  = packet.AsSpan(0, nonceLen).ToArray();
            byte[] tag    = packet.AsSpan(nonceLen, tagLen).ToArray();
            byte[] cipher = packet.AsSpan(nonceLen + tagLen).ToArray();
            byte[] plain  = new byte[cipher.Length];

            using var aes = new System.Security.Cryptography.AesGcm(key, tagLen);
            aes.Decrypt(nonce, cipher, tag, plain);
            return plain;
        }
        catch
        {
            return null;
        }
    }

    private static bool LooksLikeIncompletePayload(string? error)
    {
        if (string.IsNullOrWhiteSpace(error))
            return false;

        var lower = error.ToLowerInvariant();
        return lower.Contains("end of data")
            || lower.Contains("unexpected end")
            || lower.Contains("reached end")
            || lower.Contains("end of stream")
            || lower.Contains("incomplete bootstrap")
            || lower.Contains("empty bootstrap payload");
    }

    private async Task<Result<byte[]>> TrySyncWithReconnectAsync(byte[]? preReadPayload, int timeoutSeconds)
    {
        var first = await _client.SyncFromAdminAsync(preReadPayload);
        if (first.IsOk)
            return first;

        await _client.DisconnectAsync();
        await Task.Delay(350);

        var reconnect = await _client.EnsureConnectedAsync(timeoutSeconds);
        if (reconnect.IsFail)
            return Result<byte[]>.Fail(first.Error ?? reconnect.Error ?? "Sync failed");

        var second = await _client.SyncFromAdminAsync(preReadPayload);
        if (second.IsOk)
            return second;

        return Result<byte[]>.Fail(second.Error ?? first.Error ?? "Sync failed");
    }

    private static bool IsPollingClosed(LocalEvent localEvent)
    {
        if (string.IsNullOrWhiteSpace(localEvent.FinishedAt))
            return false;

        return !DateTime.TryParse(localEvent.GraceEndsAt, out var graceEndsAt)
               || DateTime.UtcNow > graceEndsAt.ToUniversalTime();
    }

    private sealed record SyncRequestDto(int EventId, int JudgeId, int sinceSeq);
}

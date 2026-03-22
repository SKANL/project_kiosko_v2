using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Nodus.Admin.Application.DTOs;
using Nodus.Admin.Application.Interfaces.Persistence;
using Nodus.Admin.Application.Interfaces.Services;
using Nodus.Admin.Domain.Common;

namespace Nodus.Admin.Application.UseCases.Events;

/// <summary>
/// Builds the bootstrap payload stored in NODUS_BOOTSTRAP_READ (byte prefix 0x03).
/// Called whenever event/project/judge data is modified.
///
/// Security: the compressed JSON payload is AES-256-GCM encrypted with the per-event
/// SharedKey so that passive BLE observers cannot read the project list or rubric
/// (Decision #44, Doc 04 §1A).
/// </summary>
public sealed class BuildBootstrapPayloadUseCase
{
    private readonly IEventRepository      _events;
    private readonly IProjectRepository    _projects;
    private readonly IJudgeRepository      _judges;
    private readonly IBleGattServerService _gatt;

    public BuildBootstrapPayloadUseCase(
        IEventRepository      events,
        IProjectRepository    projects,
        IJudgeRepository      judges,
        IBleGattServerService gatt)
    {
        _events   = events;
        _projects = projects;
        _judges   = judges;
        _gatt     = gatt;
    }

    public async Task<Result> ExecuteAsync(int eventId, int sinceSeq = 0)
    {
        var evtR  = await _events.GetByIdAsync(eventId);
        if (evtR.IsFail) return Result.Fail(evtR.Error!);

        var projR = sinceSeq > 0
            ? await _projects.GetByEventSinceSequenceAsync(eventId, sinceSeq)
            : await _projects.GetByEventAsync(eventId);
        if (projR.IsFail) return Result.Fail(projR.Error!);

        var judgeR = await _judges.GetByEventAsync(eventId);
        if (judgeR.IsFail) return Result.Fail(judgeR.Error!);

        var dto = new BootstrapPayloadDto(
            sinceSeq > 0 ? "project_sync" : "event_bootstrap",
            evtR.Value!.Id,
            evtR.Value!.Name,
            evtR.Value!.Institution,
            evtR.Value!.Date,
            evtR.Value!.PublicKeyBase64,
            evtR.Value!.Status.ToString(),
            evtR.Value!.FinishedAt,
            evtR.Value!.GraceEndsAt,
            sinceSeq,
            projR.Value!.Select(p => new ProjectInfoDto(p.Id, p.Name, p.Description, p.Category, p.TeamMembers, p.SortOrder, p.ProjectCode, p.SequenceNumber, p.StandNumber, p.GithubLink, p.VideoLink, p.TechStack, p.Objetivos)).ToList(),
            judgeR.Value!.Select(j => new JudgeInfoDto(j.Id, j.Name, j.PublicKeyBase64)).ToList(),
            evtR.Value!.RubricVersion,
            evtR.Value!.RubricJson
        );

        string json      = JsonSerializer.Serialize(dto);
        byte[] jsonBytes = Encoding.UTF8.GetBytes(json);

        // Step 1: GZip-compress to reduce BLE transfer size.
        using var ms = new MemoryStream();
        using (var gz = new GZipStream(ms, CompressionLevel.Optimal))
            gz.Write(jsonBytes);
        byte[] compressed = ms.ToArray();

        // Step 2: AES-256-GCM encrypt the compressed bytes (Decision #44).
        // The prefix byte is placed in the clear so the Judge knows the payload type.
        //
        // v2 envelope (encrypted):
        //   [prefix:1][0x02][jsonLen:4][compLen:4][aesLen:4][nonce(12)|tag(16)|cipher...]
        //
        // v1 envelope (legacy, no SharedKey):
        //   [prefix:1][0x01][jsonLen:4][compLen:4][compressedBytes...]
        byte  prefix    = sinceSeq > 0 ? (byte)0x04 : (byte)0x03;
        string sharedKey = evtR.Value!.SharedKeyBase64;
        byte[] payload;

        if (!string.IsNullOrWhiteSpace(sharedKey))
        {
            byte[]? encrypted = EncryptAesGcm(compressed, sharedKey);
            if (encrypted is null)
                return Result.Fail("Bootstrap AES-GCM encryption failed: bad shared key.");

            payload = new byte[14 + encrypted.Length];
            payload[0] = prefix;
            payload[1] = 0x02;   // version = 2 (encrypted)
            BitConverter.TryWriteBytes(payload.AsSpan(2,  4), jsonBytes.Length);
            BitConverter.TryWriteBytes(payload.AsSpan(6,  4), compressed.Length);
            BitConverter.TryWriteBytes(payload.AsSpan(10, 4), encrypted.Length);
            encrypted.CopyTo(payload, 14);
        }
        else
        {
            // v1 fallback — unencrypted for backwards compat with events that have no SharedKey.
            payload = new byte[10 + compressed.Length];
            payload[0] = prefix;
            payload[1] = 0x01;   // version = 1 (unencrypted)
            BitConverter.TryWriteBytes(payload.AsSpan(2, 4), jsonBytes.Length);
            BitConverter.TryWriteBytes(payload.AsSpan(6, 4), compressed.Length);
            compressed.CopyTo(payload, 10);
        }

        _gatt.UpdateBootstrapPayload(payload);
        return Result.Ok();
    }

    /// <summary>
    /// AES-256-GCM encrypt <paramref name="plainBytes"/> with the given Base64 key.
    /// Output format: [nonce:12][tag:16][ciphertext...]
    /// Returns null on failure.
    /// </summary>
    private static byte[]? EncryptAesGcm(byte[] plainBytes, string sharedKeyBase64)
    {
        try
        {
            byte[] key   = Convert.FromBase64String(sharedKeyBase64);
            byte[] nonce = new byte[AesGcm.NonceByteSizes.MinSize];   // 12 bytes
            RandomNumberGenerator.Fill(nonce);

            byte[] cipher = new byte[plainBytes.Length];
            byte[] tag    = new byte[AesGcm.TagByteSizes.MaxSize];    // 16 bytes

            using var aes = new AesGcm(key, AesGcm.TagByteSizes.MaxSize);
            aes.Encrypt(nonce, plainBytes, cipher, tag);

            // Pack: nonce | tag | cipher
            byte[] packed = new byte[nonce.Length + tag.Length + cipher.Length];
            Buffer.BlockCopy(nonce,  0, packed, 0,                         nonce.Length);
            Buffer.BlockCopy(tag,    0, packed, nonce.Length,              tag.Length);
            Buffer.BlockCopy(cipher, 0, packed, nonce.Length + tag.Length, cipher.Length);
            return packed;
        }
        catch
        {
            return null;
        }
    }
}

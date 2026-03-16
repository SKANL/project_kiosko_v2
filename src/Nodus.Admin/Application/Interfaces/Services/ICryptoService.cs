using Nodus.Admin.Domain.Common;

namespace Nodus.Admin.Application.Interfaces.Services;

/// <summary>
/// Ed25519 signing/verification + AES-256-GCM encryption (BouncyCastle).
/// </summary>
public interface ICryptoService
{
    /// <summary>Generate a new Ed25519 keypair. Returns (publicKeyBase64, privateKeyBase64).</summary>
    (string PublicKeyBase64, string PrivateKeyBase64) GenerateKeyPair();

    /// <summary>Sign data. Returns Base64 signature.</summary>
    Result<string> Sign(byte[] data, string privateKeyBase64);

    /// <summary>Verify an Ed25519 signature.</summary>
    bool Verify(byte[] data, string signatureBase64, string publicKeyBase64);

    /// <summary>Encrypt private key using AES-256-GCM with a PIN-derived key (PBKDF2).</summary>
    Result<string> EncryptPrivateKey(string privateKeyBase64, string pin);

    /// <summary>Decrypt private key previously encrypted by EncryptPrivateKey.</summary>
    Result<string> DecryptPrivateKey(string encryptedBase64, string pin);

    /// <summary>Hash a PIN using PBKDF2-SHA256 (100 000 iterations). Returns Base64.</summary>
    string HashPin(string pin);

    /// <summary>Verify a PIN against a stored PBKDF2 hash.</summary>
    bool VerifyPin(string pin, string hashBase64);

    /// <summary>Encrypt a short UTF-8 payload using a password-derived key and context salt.</summary>
    Result<string> EncryptPayloadWithPassword(string plainText, string password, string context);

    /// <summary>Decrypt a short UTF-8 payload previously encrypted with EncryptPayloadWithPassword.</summary>
    Result<string> DecryptPayloadWithPassword(string encryptedBase64, string password, string context);

    /// <summary>Encrypt a short UTF-8 payload using a pre-shared AES-256 key encoded as Base64.</summary>
    Result<string> EncryptPayloadWithSharedKey(string plainText, string sharedKeyBase64);

    /// <summary>Decrypt a short UTF-8 payload previously encrypted with EncryptPayloadWithSharedKey.</summary>
    Result<string> DecryptPayloadWithSharedKey(string encryptedBase64, string sharedKeyBase64);
}

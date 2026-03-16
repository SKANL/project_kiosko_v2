using Nodus.Admin.Application.Interfaces.Services;
using Nodus.Admin.Domain.Common;
using System.Security.Cryptography;
using Org.BouncyCastle.Crypto.Generators;
using Org.BouncyCastle.Crypto.Parameters;
using Org.BouncyCastle.Crypto.Signers;
using Org.BouncyCastle.Crypto.Engines;
using Org.BouncyCastle.Crypto.Modes;
using Org.BouncyCastle.Crypto;
using Org.BouncyCastle.Security;

namespace Nodus.Admin.Infrastructure.Crypto;

/// <summary>
/// Cryptographic services using BouncyCastle.Cryptography:
///   - Ed25519 key generation, signing, and verification
///   - AES-256-GCM encryption for private key at-rest storage
///   - PBKDF2-SHA256 (100 000 iterations) for PIN hashing and key derivation
/// </summary>
public sealed class CryptoService : ICryptoService
{
    private const int Pbkdf2Iterations = 100_000;
    private const int GcmTagBits       = 128;
    private const int SaltBytes        = 16;
    private const int NonceBytes       = 12;

    private static readonly SecureRandom _rng = new();

    // ── Key Generation ────────────────────────────────────────────────────
    public (string PublicKeyBase64, string PrivateKeyBase64) GenerateKeyPair()
    {
        var gen = new Ed25519KeyPairGenerator();
        gen.Init(new Ed25519KeyGenerationParameters(_rng));
        var pair = gen.GenerateKeyPair();

        var pub  = (Ed25519PublicKeyParameters)pair.Public;
        var priv = (Ed25519PrivateKeyParameters)pair.Private;

        return (Convert.ToBase64String(pub.GetEncoded()),
                Convert.ToBase64String(priv.GetEncoded()));
    }

    // ── Ed25519 Sign / Verify ─────────────────────────────────────────────
    public Result<string> Sign(byte[] data, string privateKeyBase64)
    {
        try
        {
            var privBytes = Convert.FromBase64String(privateKeyBase64);
            var privKey   = new Ed25519PrivateKeyParameters(privBytes);

            var signer = new Ed25519Signer();
            signer.Init(true, privKey);
            signer.BlockUpdate(data, 0, data.Length);
            byte[] sig = signer.GenerateSignature();

            return Result<string>.Ok(Convert.ToBase64String(sig));
        }
        catch (Exception ex)
        {
            return Result<string>.Fail($"Sign failed: {ex.Message}");
        }
    }

    public bool Verify(byte[] data, string signatureBase64, string publicKeyBase64)
    {
        if (string.IsNullOrEmpty(signatureBase64) || string.IsNullOrEmpty(publicKeyBase64))
            return false;

        try
        {
            var pubBytes = Convert.FromBase64String(publicKeyBase64);
            var sigBytes = Convert.FromBase64String(signatureBase64);
            var pubKey   = new Ed25519PublicKeyParameters(pubBytes);

            var verifier = new Ed25519Signer();
            verifier.Init(false, pubKey);
            verifier.BlockUpdate(data, 0, data.Length);
            return verifier.VerifySignature(sigBytes);
        }
        catch (FormatException ex)
        {
            System.Diagnostics.Debug.WriteLine(
                $"CryptoService.Verify: invalid Base64 — {ex.Message}");
            return false;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine(
                $"CryptoService.Verify: unexpected error — {ex.GetType().Name}: {ex.Message}");
            return false;
        }
    }

    // ── AES-256-GCM Encrypt / Decrypt private key ─────────────────────────
    public Result<string> EncryptPrivateKey(string privateKeyBase64, string pin)
    {
        try
        {
            byte[] salt  = GenerateRandom(SaltBytes);
            byte[] nonce = GenerateRandom(NonceBytes);
            byte[] key   = DeriveKey(pin, salt, 32);
            byte[] plain = Convert.FromBase64String(privateKeyBase64);

            byte[] ciphertext = AesGcmEncrypt(key, nonce, plain);

            // Layout: salt(16) | nonce(12) | ciphertext+tag
            byte[] blob = new byte[SaltBytes + NonceBytes + ciphertext.Length];
            Buffer.BlockCopy(salt,       0, blob, 0,                         SaltBytes);
            Buffer.BlockCopy(nonce,      0, blob, SaltBytes,                 NonceBytes);
            Buffer.BlockCopy(ciphertext, 0, blob, SaltBytes + NonceBytes,    ciphertext.Length);

            return Result<string>.Ok(Convert.ToBase64String(blob));
        }
        catch (Exception ex)
        {
            return Result<string>.Fail($"Encrypt failed: {ex.Message}");
        }
    }

    public Result<string> DecryptPrivateKey(string encryptedBase64, string pin)
    {
        try
        {
            byte[] blob  = Convert.FromBase64String(encryptedBase64);
            byte[] salt  = blob[..SaltBytes];
            byte[] nonce = blob[SaltBytes..(SaltBytes + NonceBytes)];
            byte[] ct    = blob[(SaltBytes + NonceBytes)..];
            byte[] key   = DeriveKey(pin, salt, 32);

            byte[] plain = AesGcmDecrypt(key, nonce, ct);
            return Result<string>.Ok(Convert.ToBase64String(plain));
        }
        catch (Exception ex)
        {
            return Result<string>.Fail($"Invalid PIN or corrupted data: {ex.Message}");
        }
    }

    // ── PIN Hash / Verify ─────────────────────────────────────────────────
    public string HashPin(string pin)
    {
        byte[] salt = GenerateRandom(SaltBytes);
        byte[] hash = DeriveKey(pin, salt, 32);

        byte[] blob = new byte[SaltBytes + hash.Length];
        Buffer.BlockCopy(salt, 0, blob, 0,        SaltBytes);
        Buffer.BlockCopy(hash, 0, blob, SaltBytes, hash.Length);

        return Convert.ToBase64String(blob);
    }

    public bool VerifyPin(string pin, string hashBase64)
    {
        try
        {
            byte[] blob = Convert.FromBase64String(hashBase64);
            byte[] salt = blob[..SaltBytes];
            byte[] stored = blob[SaltBytes..];
            byte[] computed = DeriveKey(pin, salt, 32);
            return CryptographicEquals(stored, computed);
        }
        catch { return false; }
    }

    public Result<string> EncryptPayloadWithPassword(string plainText, string password, string context)
    {
        try
        {
            byte[] salt  = DeriveContextSalt(context);
            byte[] nonce = GenerateRandom(NonceBytes);
            byte[] key   = DeriveKey(password, salt, 32);
            byte[] plain = System.Text.Encoding.UTF8.GetBytes(plainText);
            byte[] cipher = AesGcmEncrypt(key, nonce, plain);

            byte[] blob = new byte[NonceBytes + cipher.Length];
            Buffer.BlockCopy(nonce, 0, blob, 0, NonceBytes);
            Buffer.BlockCopy(cipher, 0, blob, NonceBytes, cipher.Length);
            return Result<string>.Ok(Convert.ToBase64String(blob));
        }
        catch (Exception ex)
        {
            return Result<string>.Fail($"Encrypt payload failed: {ex.Message}");
        }
    }

    public Result<string> DecryptPayloadWithPassword(string encryptedBase64, string password, string context)
    {
        try
        {
            byte[] blob  = Convert.FromBase64String(encryptedBase64);
            byte[] nonce = blob[..NonceBytes];
            byte[] ct    = blob[NonceBytes..];
            byte[] salt  = DeriveContextSalt(context);
            byte[] key   = DeriveKey(password, salt, 32);
            byte[] plain = AesGcmDecrypt(key, nonce, ct);
            return Result<string>.Ok(System.Text.Encoding.UTF8.GetString(plain));
        }
        catch (Exception ex)
        {
            return Result<string>.Fail($"Decrypt payload failed: {ex.Message}");
        }
    }

    public Result<string> EncryptPayloadWithSharedKey(string plainText, string sharedKeyBase64)
    {
        try
        {
            byte[] nonce = GenerateRandom(NonceBytes);
            byte[] key   = Convert.FromBase64String(sharedKeyBase64);
            byte[] plain = System.Text.Encoding.UTF8.GetBytes(plainText);
            byte[] cipher = AesGcmEncrypt(key, nonce, plain);

            byte[] blob = new byte[NonceBytes + cipher.Length];
            Buffer.BlockCopy(nonce, 0, blob, 0, NonceBytes);
            Buffer.BlockCopy(cipher, 0, blob, NonceBytes, cipher.Length);
            return Result<string>.Ok(Convert.ToBase64String(blob));
        }
        catch (Exception ex)
        {
            return Result<string>.Fail($"Encrypt shared payload failed: {ex.Message}");
        }
    }

    public Result<string> DecryptPayloadWithSharedKey(string encryptedBase64, string sharedKeyBase64)
    {
        try
        {
            byte[] blob  = Convert.FromBase64String(encryptedBase64);
            byte[] nonce = blob[..NonceBytes];
            byte[] ct    = blob[NonceBytes..];
            byte[] key   = Convert.FromBase64String(sharedKeyBase64);
            byte[] plain = AesGcmDecrypt(key, nonce, ct);
            return Result<string>.Ok(System.Text.Encoding.UTF8.GetString(plain));
        }
        catch (Exception ex)
        {
            return Result<string>.Fail($"Decrypt shared payload failed: {ex.Message}");
        }
    }

    // ── Private Helpers ───────────────────────────────────────────────────
    private static byte[] DeriveKey(string password, byte[] salt, int keyLenBytes)
    {
        var gen = new Pkcs5S2ParametersGenerator(new Org.BouncyCastle.Crypto.Digests.Sha256Digest());
        gen.Init(System.Text.Encoding.UTF8.GetBytes(password), salt, Pbkdf2Iterations);
        return ((KeyParameter)gen.GenerateDerivedMacParameters(keyLenBytes * 8)).GetKey();
    }

    private static byte[] AesGcmEncrypt(byte[] key, byte[] nonce, byte[] plaintext)
    {
        var cipher = new GcmBlockCipher(new AesEngine());
        cipher.Init(true, new AeadParameters(new KeyParameter(key), GcmTagBits, nonce));
        byte[] output = new byte[cipher.GetOutputSize(plaintext.Length)];
        int len = cipher.ProcessBytes(plaintext, 0, plaintext.Length, output, 0);
        cipher.DoFinal(output, len);
        return output;
    }

    private static byte[] AesGcmDecrypt(byte[] key, byte[] nonce, byte[] ciphertext)
    {
        var cipher = new GcmBlockCipher(new AesEngine());
        cipher.Init(false, new AeadParameters(new KeyParameter(key), GcmTagBits, nonce));
        byte[] output = new byte[cipher.GetOutputSize(ciphertext.Length)];
        int len = cipher.ProcessBytes(ciphertext, 0, ciphertext.Length, output, 0);
        cipher.DoFinal(output, len);
        return output;
    }

    private static byte[] GenerateRandom(int length)
    {
        byte[] bytes = new byte[length];
        _rng.NextBytes(bytes);
        return bytes;
    }

    private static byte[] DeriveContextSalt(string context)
    {
        byte[] hash = SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(context));
        return hash[..SaltBytes];
    }

    /// <summary>Constant-time comparison to prevent timing attacks.</summary>
    private static bool CryptographicEquals(byte[] a, byte[] b)
    {
        if (a.Length != b.Length) return false;
        int diff = 0;
        for (int i = 0; i < a.Length; i++) diff |= a[i] ^ b[i];
        return diff == 0;
    }
}

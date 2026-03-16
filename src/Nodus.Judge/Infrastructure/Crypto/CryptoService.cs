using Nodus.Judge.Application.Interfaces.Services;
using Nodus.Judge.Domain.Common;
using System.Security.Cryptography;
using Org.BouncyCastle.Crypto;
using Org.BouncyCastle.Crypto.Engines;
using Org.BouncyCastle.Crypto.Generators;
using Org.BouncyCastle.Crypto.Modes;
using Org.BouncyCastle.Crypto.Parameters;
using Org.BouncyCastle.Crypto.Signers;
using Org.BouncyCastle.Security;

namespace Nodus.Judge.Infrastructure.Crypto;

/// <summary>
/// Cryptographic services for the Judge app — identical algorithms to Admin.
/// Ed25519 + AES-256-GCM + PBKDF2-SHA256 via BouncyCastle.
/// </summary>
public sealed class CryptoService : ICryptoService
{
    private const int Pbkdf2Iterations = 100_000;
    private const int GcmTagBits       = 128;
    private const int SaltBytes        = 16;
    private const int NonceBytes       = 12;

    private static readonly SecureRandom _rng = new();

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

    public Result<string> Sign(byte[] data, string privateKeyBase64)
    {
        try
        {
            var privKey = new Ed25519PrivateKeyParameters(Convert.FromBase64String(privateKeyBase64));
            var signer  = new Ed25519Signer();
            signer.Init(true, privKey);
            signer.BlockUpdate(data, 0, data.Length);
            return Result<string>.Ok(Convert.ToBase64String(signer.GenerateSignature()));
        }
        catch (Exception ex) { return Result<string>.Fail($"Sign: {ex.Message}"); }
    }

    public bool Verify(byte[] data, string signatureBase64, string publicKeyBase64)
    {
        try
        {
            var pubKey   = new Ed25519PublicKeyParameters(Convert.FromBase64String(publicKeyBase64));
            var verifier = new Ed25519Signer();
            verifier.Init(false, pubKey);
            verifier.BlockUpdate(data, 0, data.Length);
            return verifier.VerifySignature(Convert.FromBase64String(signatureBase64));
        }
        catch { return false; }
    }

    public Result<string> EncryptPrivateKey(string privateKeyBase64, string pin)
    {
        try
        {
            byte[] salt  = Rand(SaltBytes);
            byte[] nonce = Rand(NonceBytes);
            byte[] key   = Pbkdf2(pin, salt, 32);
            byte[] ct    = GcmEncrypt(key, nonce, Convert.FromBase64String(privateKeyBase64));

            byte[] blob = new byte[SaltBytes + NonceBytes + ct.Length];
            Buffer.BlockCopy(salt,  0, blob, 0,                       SaltBytes);
            Buffer.BlockCopy(nonce, 0, blob, SaltBytes,               NonceBytes);
            Buffer.BlockCopy(ct,    0, blob, SaltBytes + NonceBytes,  ct.Length);
            return Result<string>.Ok(Convert.ToBase64String(blob));
        }
        catch (Exception ex) { return Result<string>.Fail(ex.Message); }
    }

    public Result<string> DecryptPrivateKey(string encryptedBase64, string pin)
    {
        try
        {
            byte[] blob  = Convert.FromBase64String(encryptedBase64);
            byte[] salt  = blob[..SaltBytes];
            byte[] nonce = blob[SaltBytes..(SaltBytes + NonceBytes)];
            byte[] ct    = blob[(SaltBytes + NonceBytes)..];
            byte[] plain = GcmDecrypt(Pbkdf2(pin, salt, 32), nonce, ct);
            return Result<string>.Ok(Convert.ToBase64String(plain));
        }
        catch (Exception ex) { return Result<string>.Fail($"Invalid PIN: {ex.Message}"); }
    }

    public string HashPin(string pin)
    {
        byte[] salt = Rand(SaltBytes);
        byte[] hash = Pbkdf2(pin, salt, 32);
        byte[] blob = new byte[SaltBytes + 32];
        Buffer.BlockCopy(salt, 0, blob, 0,        SaltBytes);
        Buffer.BlockCopy(hash, 0, blob, SaltBytes, 32);
        return Convert.ToBase64String(blob);
    }

    public bool VerifyPin(string pin, string hashBase64)
    {
        try
        {
            byte[] blob     = Convert.FromBase64String(hashBase64);
            byte[] salt     = blob[..SaltBytes];
            byte[] stored   = blob[SaltBytes..];
            byte[] computed = Pbkdf2(pin, salt, 32);
            int diff = 0;
            for (int i = 0; i < stored.Length; i++) diff |= stored[i] ^ computed[i];
            return diff == 0;
        }
        catch { return false; }
    }

    public Result<string> EncryptPayloadWithPassword(string plainText, string password, string context)
    {
        try
        {
            byte[] salt  = ContextSalt(context);
            byte[] nonce = Rand(NonceBytes);
            byte[] key   = Pbkdf2(password, salt, 32);
            byte[] ct    = GcmEncrypt(key, nonce, System.Text.Encoding.UTF8.GetBytes(plainText));

            byte[] blob = new byte[NonceBytes + ct.Length];
            Buffer.BlockCopy(nonce, 0, blob, 0, NonceBytes);
            Buffer.BlockCopy(ct, 0, blob, NonceBytes, ct.Length);
            return Result<string>.Ok(Convert.ToBase64String(blob));
        }
        catch (Exception ex) { return Result<string>.Fail($"Encrypt payload: {ex.Message}"); }
    }

    public Result<string> DecryptPayloadWithPassword(string encryptedBase64, string password, string context)
    {
        try
        {
            byte[] blob  = Convert.FromBase64String(encryptedBase64);
            byte[] nonce = blob[..NonceBytes];
            byte[] ct    = blob[NonceBytes..];
            byte[] key   = Pbkdf2(password, ContextSalt(context), 32);
            byte[] plain = GcmDecrypt(key, nonce, ct);
            return Result<string>.Ok(System.Text.Encoding.UTF8.GetString(plain));
        }
        catch (Exception ex) { return Result<string>.Fail($"Decrypt payload: {ex.Message}"); }
    }

    public Result<string> EncryptPayloadWithSharedKey(string plainText, string sharedKeyBase64)
    {
        try
        {
            byte[] nonce = Rand(NonceBytes);
            byte[] ct = GcmEncrypt(
                Convert.FromBase64String(sharedKeyBase64),
                nonce,
                System.Text.Encoding.UTF8.GetBytes(plainText));

            byte[] blob = new byte[NonceBytes + ct.Length];
            Buffer.BlockCopy(nonce, 0, blob, 0, NonceBytes);
            Buffer.BlockCopy(ct, 0, blob, NonceBytes, ct.Length);
            return Result<string>.Ok(Convert.ToBase64String(blob));
        }
        catch (Exception ex) { return Result<string>.Fail($"Encrypt shared payload: {ex.Message}"); }
    }

    public Result<string> DecryptPayloadWithSharedKey(string encryptedBase64, string sharedKeyBase64)
    {
        try
        {
            byte[] blob  = Convert.FromBase64String(encryptedBase64);
            byte[] nonce = blob[..NonceBytes];
            byte[] ct    = blob[NonceBytes..];
            byte[] plain = GcmDecrypt(Convert.FromBase64String(sharedKeyBase64), nonce, ct);
            return Result<string>.Ok(System.Text.Encoding.UTF8.GetString(plain));
        }
        catch (Exception ex) { return Result<string>.Fail($"Decrypt shared payload: {ex.Message}"); }
    }

    private static byte[] Pbkdf2(string password, byte[] salt, int bytes)
    {
        var gen = new Pkcs5S2ParametersGenerator(new Org.BouncyCastle.Crypto.Digests.Sha256Digest());
        gen.Init(System.Text.Encoding.UTF8.GetBytes(password), salt, Pbkdf2Iterations);
        return ((KeyParameter)gen.GenerateDerivedMacParameters(bytes * 8)).GetKey();
    }

    private static byte[] GcmEncrypt(byte[] key, byte[] nonce, byte[] plain)
    {
        var c = new GcmBlockCipher(new AesEngine());
        c.Init(true, new AeadParameters(new KeyParameter(key), GcmTagBits, nonce));
        byte[] o = new byte[c.GetOutputSize(plain.Length)];
        c.DoFinal(o, c.ProcessBytes(plain, 0, plain.Length, o, 0));
        return o;
    }

    private static byte[] GcmDecrypt(byte[] key, byte[] nonce, byte[] ct)
    {
        var c = new GcmBlockCipher(new AesEngine());
        c.Init(false, new AeadParameters(new KeyParameter(key), GcmTagBits, nonce));
        byte[] o = new byte[c.GetOutputSize(ct.Length)];
        c.DoFinal(o, c.ProcessBytes(ct, 0, ct.Length, o, 0));
        return o;
    }

    private static byte[] Rand(int n) { var b = new byte[n]; _rng.NextBytes(b); return b; }

    private static byte[] ContextSalt(string context)
    {
        byte[] hash = SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(context));
        return hash[..SaltBytes];
    }
}

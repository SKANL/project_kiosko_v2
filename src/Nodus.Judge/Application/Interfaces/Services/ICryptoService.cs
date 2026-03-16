using Nodus.Judge.Domain.Common;

namespace Nodus.Judge.Application.Interfaces.Services;

public interface ICryptoService
{
    (string PublicKeyBase64, string PrivateKeyBase64) GenerateKeyPair();

    Result<string> Sign(byte[] data, string privateKeyBase64);

    bool Verify(byte[] data, string signatureBase64, string publicKeyBase64);

    Result<string> EncryptPrivateKey(string privateKeyBase64, string pin);

    Result<string> DecryptPrivateKey(string encryptedBase64, string pin);

    string HashPin(string pin);

    bool VerifyPin(string pin, string hashBase64);

    Result<string> EncryptPayloadWithPassword(string plainText, string password, string context);

    Result<string> DecryptPayloadWithPassword(string encryptedBase64, string password, string context);

    Result<string> EncryptPayloadWithSharedKey(string plainText, string sharedKeyBase64);

    Result<string> DecryptPayloadWithSharedKey(string encryptedBase64, string sharedKeyBase64);
}

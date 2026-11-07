using System.Security.Cryptography;
using System.Text;
using Clinic.Application.Common.Interfaces;

namespace Clinic.Infrastructure.Security.Encryption;

/// AES-256-GCM: تشفير مع تحقق من سلامة البيانات.
/// الصيغة المخزنة: Base64( nonce[12] + tag[16] + cipherText )
/// كل تشفير بيستخدم nonce عشوائي جديد، فنفس النص بيطلع مشفر بشكل مختلف كل مرة.
public sealed class AesGcmFieldEncryptor : IFieldEncryptor
{
    private const int NonceSize = 12;
    private const int TagSize = 16;

    private readonly byte[] _key;

    public AesGcmFieldEncryptor(EncryptionSettings settings)
    {
        if (settings.Key is null || settings.Key.Length != 32)
        {
            throw new ArgumentException("Encryption key must be exactly 32 bytes.", nameof(settings));
        }

        _key = settings.Key;
    }

    public string? Encrypt(string? plainText)
    {
        if (string.IsNullOrEmpty(plainText))
        {
            return plainText;
        }

        var plain = Encoding.UTF8.GetBytes(plainText);
        var nonce = RandomNumberGenerator.GetBytes(NonceSize);
        var cipher = new byte[plain.Length];
        var tag = new byte[TagSize];

        using var aes = new AesGcm(_key, TagSize);
        aes.Encrypt(nonce, plain, cipher, tag);

        var result = new byte[NonceSize + TagSize + cipher.Length];
        Buffer.BlockCopy(nonce, 0, result, 0, NonceSize);
        Buffer.BlockCopy(tag, 0, result, NonceSize, TagSize);
        Buffer.BlockCopy(cipher, 0, result, NonceSize + TagSize, cipher.Length);

        return Convert.ToBase64String(result);
    }

    public string? Decrypt(string? cipherText)
    {
        if (string.IsNullOrEmpty(cipherText))
        {
            return cipherText;
        }

        var data = Convert.FromBase64String(cipherText);
        if (data.Length < NonceSize + TagSize)
        {
            throw new CryptographicException("Encrypted value is too short.");
        }

        var nonce = data.AsSpan(0, NonceSize);
        var tag = data.AsSpan(NonceSize, TagSize);
        var cipher = data.AsSpan(NonceSize + TagSize);
        var plain = new byte[cipher.Length];

        using var aes = new AesGcm(_key, TagSize);
        aes.Decrypt(nonce, cipher, tag, plain);

        return Encoding.UTF8.GetString(plain);
    }
}

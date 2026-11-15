using System.Security.Cryptography;
using Clinic.Infrastructure.Security.Encryption;

namespace Clinic.Application.Tests.Medical;

public class FieldEncryptorTests
{
    private static AesGcmFieldEncryptor Create(byte? fill = 7) =>
        new(new EncryptionSettings(Enumerable.Repeat(fill ?? (byte)7, 32).ToArray()));

    [Fact]
    public void Encrypt_then_Decrypt_returns_original_text()
    {
        var sut = Create();

        var cipher = sut.Encrypt("سكر وضغط");

        Assert.Equal("سكر وضغط", sut.Decrypt(cipher));
    }

    [Fact]
    public void Encrypted_value_is_not_plain_text()
    {
        var sut = Create();

        var cipher = sut.Encrypt("Penicillin allergy");

        Assert.NotEqual("Penicillin allergy", cipher);
        Assert.DoesNotContain("Penicillin", cipher);
    }

    [Fact]
    public void Same_text_encrypts_differently_each_time()
    {
        var sut = Create();

        Assert.NotEqual(sut.Encrypt("Aspirin"), sut.Encrypt("Aspirin"));
    }

    [Fact]
    public void Null_and_empty_pass_through_unchanged()
    {
        var sut = Create();

        Assert.Null(sut.Encrypt(null));
        Assert.Equal("", sut.Encrypt(""));
        Assert.Null(sut.Decrypt(null));
        Assert.Equal("", sut.Decrypt(""));
    }

    [Fact]
    public void Tampered_cipher_is_rejected()
    {
        var sut = Create();
        var bytes = Convert.FromBase64String(sut.Encrypt("Insulin")!);
        bytes[^1] ^= 0xFF;

        Assert.ThrowsAny<CryptographicException>(() => sut.Decrypt(Convert.ToBase64String(bytes)));
    }

    [Fact]
    public void Wrong_key_cannot_decrypt()
    {
        var cipher = Create(1).Encrypt("Insulin");

        Assert.ThrowsAny<CryptographicException>(() => Create(2).Decrypt(cipher));
    }

    [Fact]
    public void Key_must_be_32_bytes()
    {
        Assert.Throws<ArgumentException>(() => new AesGcmFieldEncryptor(new EncryptionSettings(new byte[16])));
    }
}

namespace Clinic.Application.Common.Interfaces;

/// تشفير وفك تشفير الحقول الحساسة (السجل الطبي، الرسائل...).
/// الـ null والنص الفاضي بيرجعوا زي ما هما من غير تشفير.
public interface IFieldEncryptor
{
    string? Encrypt(string? plainText);
    string? Decrypt(string? cipherText);
}

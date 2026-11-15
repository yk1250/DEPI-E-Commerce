namespace Clinic.Infrastructure.Security.Encryption;

/// الإعدادات بتيجي من User Secrets: Encryption:Key
/// المفتاح لازم يكون Base64 لـ 32 بايت بالظبط (AES-256).
public sealed record EncryptionSettings(byte[] Key);

using Clinic.Application.Common.Exceptions;

namespace Clinic.Application.Features.Attachments;

/// نتيجة التحقق: اسم نضيف + امتداد + نوع محتوى متأكدين منهم.
public sealed record ValidatedAttachment(string SafeFileName, string Extension, string ContentType);

/// قواعد قبول المرفقات (PDF وصور JPG/PNG بس، حد أقصى 10 ميجا).
/// التحقق بيتم على أربع مستويات: الحجم، الامتداد، نوع المحتوى المُعلن، وأول بايتات الملف الحقيقية
/// (عشان ملف exe متسمّي .pdf يتكشف).
public static class AttachmentRules
{
    public const long MaxSizeBytes = 10L * 1024 * 1024;

    /// عدد بايتات بداية الملف اللي الـ Controller لازم يقراها ويمررها لـ Validate.
    public const int HeaderBytesNeeded = 8;

    private const int MaxFileNameLength = 255; // نفس حد عمود FileName في الداتابيز

    private sealed record FileKind(string ContentType, byte[] Signature);

    private static readonly Dictionary<string, FileKind> Allowed = new(StringComparer.OrdinalIgnoreCase)
    {
        [".pdf"] = new FileKind("application/pdf", new byte[] { 0x25, 0x50, 0x44, 0x46, 0x2D }),
        [".png"] = new FileKind("image/png", new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }),
        [".jpg"] = new FileKind("image/jpeg", new byte[] { 0xFF, 0xD8, 0xFF }),
        [".jpeg"] = new FileKind("image/jpeg", new byte[] { 0xFF, 0xD8, 0xFF }),
    };

    public static ValidatedAttachment Validate(
        string? fileName,
        string? contentType,
        long length,
        ReadOnlySpan<byte> header)
    {
        var safeName = SanitizeFileName(fileName);

        if (length <= 0)
        {
            throw new AppValidationException("The file is empty.");
        }

        if (length > MaxSizeBytes)
        {
            throw new AppValidationException(
                $"The file is larger than the allowed {MaxSizeBytes / (1024 * 1024)} MB.");
        }

        var extension = Path.GetExtension(safeName);
        if (!Allowed.TryGetValue(extension, out var kind))
        {
            throw new AppValidationException("Only PDF, JPG and PNG files are allowed.");
        }

        if (!string.Equals(contentType, kind.ContentType, StringComparison.OrdinalIgnoreCase))
        {
            throw new AppValidationException("The file content type does not match its extension.");
        }

        if (!header.StartsWith(kind.Signature))
        {
            throw new AppValidationException("The file content does not match its extension.");
        }

        return new ValidatedAttachment(safeName, extension.ToLowerInvariant(), kind.ContentType);
    }

    /// بيشيل أي مسار (../ أو \) وأي حروف تحكم، ويقصّ الاسم الطويل مع الحفاظ على الامتداد.
    /// الاسم ده للعرض بس، ومش بيتستخدم أبداً في مسار التخزين.
    private static string SanitizeFileName(string? fileName)
    {
        var name = Path.GetFileName((fileName ?? string.Empty).Replace('\\', '/'));
        var cleaned = new string(name.Where(c => !char.IsControl(c)).ToArray()).Trim();

        if (cleaned.Length == 0 || cleaned.All(c => c == '.'))
        {
            throw new AppValidationException("The file name is missing.");
        }

        if (cleaned.Length > MaxFileNameLength)
        {
            var ext = Path.GetExtension(cleaned);
            if (ext.Length >= MaxFileNameLength)
            {
                ext = string.Empty;
            }

            cleaned = cleaned[..(MaxFileNameLength - ext.Length)] + ext;
        }

        return cleaned;
    }
}

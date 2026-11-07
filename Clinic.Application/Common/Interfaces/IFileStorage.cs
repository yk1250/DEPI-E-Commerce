namespace Clinic.Application.Common.Interfaces;

/// تخزين الملفات (المرفقات). المسار اللي بيرجع من SaveAsync هو اللي بيتخزن في Attachment.StoredPath.
/// المسار نسبي دايماً ومش بيحتوي على أي حاجة جاية من المستخدم.
public interface IFileStorage
{
    /// يحفظ الملف ويرجّع المسار النسبي. الـ extension لازم يكون من نتيجة AttachmentRules.Validate (زي ".pdf").
    Task<string> SaveAsync(Stream content, string extension, CancellationToken ct = default);

    /// يفتح الملف للقراءة. لو مش موجود أو المسار برا مكان التخزين يرمي NotFoundException.
    Task<Stream> OpenReadAsync(string storedPath, CancellationToken ct = default);

    Task DeleteAsync(string storedPath, CancellationToken ct = default);
}

namespace Clinic.Infrastructure.Files;

/// مكان تخزين المرفقات على الجهاز. بيتظبط من الإعداد Attachments:RootPath (اختياري).
/// لازم يكون برا مجلد wwwroot عشان الملفات ما تتحملش مباشرة من غير صلاحيات.
public sealed record AttachmentStorageSettings(string RootPath);

using Clinic.Application.Common.Exceptions;
using Clinic.Application.Common.Interfaces;
using Clinic.Application.Common.Security;
using Clinic.Domain.Entities;
using Clinic.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace Clinic.Application.Features.Attachments;

/// بيانات المرفق اللي بترجع للعميل (من غير المسار الداخلي على القرص).
public sealed record AttachmentDto(
    int Id,
    int? AppointmentId,
    string FileName,
    string ContentType,
    long SizeBytes,
    DateTime UploadedAt);

/// ملف جاهز للتحميل. الـ Controller هو اللي بيقفل الـ Stream بعد الإرسال.
public sealed record AttachmentFile(Stream Content, string ContentType, string FileName);

/// رفع وتحميل المرفقات (نتايج تحاليل وأشعة...).
/// - الرفع: المريض لموعد بتاعه هو بس.
/// - التحميل: المريض صاحبه، أو دكتور معاه موعد فعلي (عن طريق IPatientAccessGuard).
public class AttachmentService
{
    /// الحد الأقصى للمرفقات في الموعد الواحد (حماية من ملء القرص).
    public const int MaxPerAppointment = 10;

    private readonly IAppDbContext _db;
    private readonly IFileStorage _storage;
    private readonly IPatientAccessGuard _guard;
    private readonly ICurrentUser _currentUser;
    private readonly TimeProvider _time;

    public AttachmentService(
        IAppDbContext db,
        IFileStorage storage,
        IPatientAccessGuard guard,
        ICurrentUser currentUser,
        TimeProvider time)
    {
        _db = db;
        _storage = storage;
        _guard = guard;
        _currentUser = currentUser;
        _time = time;
    }

    /// المريض يرفع ملف لموعد بتاعه. الـ content لازم يكون Stream بيدعم Seek (ملف الـ IFormFile بيدعم).
    public async Task<AttachmentDto> UploadAsync(
        int appointmentId,
        string? fileName,
        string? contentType,
        Stream content,
        CancellationToken ct = default)
    {
        var (patientId, userId) = RequireOwnPatient();

        // موعد مريض تاني بيتعامل كأنه مش موجود (من غير ما نكشف إنه موجود)
        var appointment = await _db.Appointments
            .AsNoTracking()
            .Where(a => a.Id == appointmentId && a.PatientId == patientId)
            .Select(a => new { a.Status })
            .FirstOrDefaultAsync(ct);

        if (appointment is null)
        {
            throw new NotFoundException("Appointment was not found.");
        }

        if (appointment.Status is AppointmentStatus.Cancelled
            or AppointmentStatus.Expired
            or AppointmentStatus.NoShow)
        {
            throw new ConflictException("Attachments cannot be added to a cancelled, expired or missed appointment.");
        }

        var existing = await _db.Attachments.CountAsync(a => a.AppointmentId == appointmentId, ct);
        if (existing >= MaxPerAppointment)
        {
            throw new ConflictException($"An appointment can have at most {MaxPerAppointment} attachments.");
        }

        if (!content.CanSeek)
        {
            throw new ArgumentException("The content stream must be seekable.", nameof(content));
        }

        var header = new byte[AttachmentRules.HeaderBytesNeeded];
        var read = await content.ReadAtLeastAsync(header, header.Length, throwOnEndOfStream: false, ct);
        var validated = Validate(fileName, contentType, content.Length, header, read);
        content.Position = 0;

        var storedPath = await _storage.SaveAsync(content, validated.Extension, ct);

        var attachment = new Attachment
        {
            PatientId = patientId,
            AppointmentId = appointmentId,
            UploadedByUserId = userId,
            FileName = validated.SafeFileName,
            StoredPath = storedPath,
            ContentType = validated.ContentType,
            SizeBytes = content.Length,
            UploadedAt = _time.GetUtcNow().UtcDateTime
        };
        _db.Attachments.Add(attachment);

        try
        {
            await _db.SaveChangesAsync(ct);
        }
        catch
        {
            // الداتابيز رفضت: نمسح الملف عشان ما يفضلش ملف يتيم على القرص
            await _storage.DeleteAsync(storedPath, CancellationToken.None);
            throw;
        }

        return new AttachmentDto(
            attachment.Id,
            attachment.AppointmentId,
            attachment.FileName,
            attachment.ContentType,
            attachment.SizeBytes,
            DateTime.SpecifyKind(attachment.UploadedAt, DateTimeKind.Utc));
    }

    /// تحميل مرفق: المريض صاحبه أو دكتور معاه موعد فعلي.
    public async Task<AttachmentFile> DownloadAsync(int attachmentId, CancellationToken ct = default)
    {
        var attachment = await _db.Attachments
            .AsNoTracking()
            .FirstOrDefaultAsync(a => a.Id == attachmentId, ct);

        if (attachment is null)
        {
            throw new NotFoundException("Attachment was not found.");
        }

        await _guard.EnsureAccessAsync(attachment.PatientId, ct);

        var stream = await _storage.OpenReadAsync(attachment.StoredPath, ct);
        return new AttachmentFile(stream, attachment.ContentType, attachment.FileName);
    }

    // دالة عادية (مش async) عشان الـ Span ما يعدّيش على await
    private static ValidatedAttachment Validate(
        string? fileName,
        string? contentType,
        long length,
        byte[] header,
        int headerLength) =>
        AttachmentRules.Validate(fileName, contentType, length, header.AsSpan(0, headerLength));

    private (int PatientId, Guid UserId) RequireOwnPatient()
    {
        if (!_currentUser.IsAuthenticated)
        {
            throw new UnauthorizedException();
        }

        if (_currentUser.Role != Roles.Patient
            || _currentUser.PatientId is not int patientId
            || _currentUser.UserId is not Guid userId)
        {
            throw new ForbiddenException("Only patients can upload attachments.");
        }

        return (patientId, userId);
    }
}

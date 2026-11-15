using Clinic.Application.Common.Exceptions;
using Clinic.Application.Common.Interfaces;
using Clinic.Application.Common.Security;
using Clinic.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Clinic.Application.Features.Medical;

/// السجل الطبي: الحقول الثلاثة بتتشفر قبل الحفظ وبتتفك عند القراءة.
/// - المريض يقرا ويكتب سجله هو بس (الـ PatientId بيتاخد من الـ token، مش من الـ request).
/// - الدكتور يقرا بس، ولمريض عنده معاه موعد فعلي (عن طريق IPatientAccessGuard).
public class MedicalHistoryService
{
    private readonly IAppDbContext _db;
    private readonly IFieldEncryptor _encryptor;
    private readonly IPatientAccessGuard _guard;
    private readonly ICurrentUser _currentUser;
    private readonly TimeProvider _time;

    public MedicalHistoryService(
        IAppDbContext db,
        IFieldEncryptor encryptor,
        IPatientAccessGuard guard,
        ICurrentUser currentUser,
        TimeProvider time)
    {
        _db = db;
        _encryptor = encryptor;
        _guard = guard;
        _currentUser = currentUser;
        _time = time;
    }

    /// سجل المريض الحالي.
    public async Task<MedicalHistoryDto> GetMineAsync(CancellationToken ct = default)
    {
        var patientId = RequireOwnPatientId();
        return await LoadAsync(patientId, ct);
    }

    /// سجل مريض معيّن للدكتور (لازم الـ guard يوافق).
    public async Task<MedicalHistoryDto> GetForPatientAsync(int patientId, CancellationToken ct = default)
    {
        await _guard.EnsureAccessAsync(patientId, ct);
        return await LoadAsync(patientId, ct);
    }

    /// المريض يكتب سجله (إنشاء أو استبدال).
    public async Task<MedicalHistoryDto> UpdateMineAsync(
        UpdateMedicalHistoryRequest request,
        CancellationToken ct = default)
    {
        var patientId = RequireOwnPatientId();

        var history = await _db.MedicalHistories.FirstOrDefaultAsync(h => h.PatientId == patientId, ct);
        if (history is null)
        {
            history = new MedicalHistory { PatientId = patientId };
            _db.MedicalHistories.Add(history);
        }

        history.ChronicConditions = _encryptor.Encrypt(Normalize(request.ChronicConditions));
        history.Allergies = _encryptor.Encrypt(Normalize(request.Allergies));
        history.CurrentMedications = _encryptor.Encrypt(Normalize(request.CurrentMedications));
        history.UpdatedAt = _time.GetUtcNow().UtcDateTime;

        try
        {
            await _db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException)
        {
            // أول كتابة اتعملت مرتين في نفس اللحظة: الـ Unique Index على PatientId رفض التانية
            throw new ConflictException("Your medical history was updated at the same time. Please try again.");
        }

        return Map(history);
    }

    private int RequireOwnPatientId()
    {
        if (!_currentUser.IsAuthenticated)
        {
            throw new UnauthorizedException();
        }

        if (_currentUser.Role != Roles.Patient || _currentUser.PatientId is not int patientId)
        {
            throw new ForbiddenException("Only patients can manage their own medical history.");
        }

        return patientId;
    }

    private async Task<MedicalHistoryDto> LoadAsync(int patientId, CancellationToken ct)
    {
        var history = await _db.MedicalHistories
            .AsNoTracking()
            .FirstOrDefaultAsync(h => h.PatientId == patientId, ct);

        return history is null
            ? new MedicalHistoryDto(patientId, null, null, null, null)
            : Map(history);
    }

    private MedicalHistoryDto Map(MedicalHistory history) => new(
        history.PatientId,
        _encryptor.Decrypt(history.ChronicConditions),
        _encryptor.Decrypt(history.Allergies),
        _encryptor.Decrypt(history.CurrentMedications),
        DateTime.SpecifyKind(history.UpdatedAt, DateTimeKind.Utc));

    /// فاضي أو مسافات بس = مفيش قيمة.
    private static string? Normalize(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

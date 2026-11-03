using Clinic.Application.Common.Exceptions;
using Clinic.Application.Common.Interfaces;
using Clinic.Domain.Entities;
using Clinic.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace Clinic.Application.Features.Schedules;

public interface IScheduleService
{
    /// جدول دكتور معتمد (عام)
    Task<IReadOnlyList<WorkingScheduleItemDto>> GetPublicScheduleAsync(int doctorId, CancellationToken ct);

    Task<IReadOnlyList<WorkingScheduleItemDto>> GetMyScheduleAsync(CancellationToken ct);
    Task<IReadOnlyList<WorkingScheduleItemDto>> ReplaceMyScheduleAsync(SaveScheduleRequest request, CancellationToken ct);

    Task<IReadOnlyList<TimeOffDto>> ListMyTimeOffsAsync(bool upcomingOnly, CancellationToken ct);
    Task<TimeOffDto> CreateMyTimeOffAsync(CreateTimeOffRequest request, CancellationToken ct);
    Task DeleteMyTimeOffAsync(int id, CancellationToken ct);
}

public class ScheduleService : IScheduleService
{
    private readonly IAppDbContext _db;
    private readonly ICurrentUser _currentUser;
    private readonly TimeProvider _time;

    public ScheduleService(IAppDbContext db, ICurrentUser currentUser, TimeProvider time)
    {
        _db = db;
        _currentUser = currentUser;
        _time = time;
    }

    // ------------------------------------------------------------------ جدول العمل

    public async Task<IReadOnlyList<WorkingScheduleItemDto>> GetPublicScheduleAsync(int doctorId, CancellationToken ct)
    {
        var visible = await _db.Doctors.AsNoTracking()
            .AnyAsync(d => d.Id == doctorId && d.IsApproved && d.Clinic.IsActive, ct);
        if (!visible)
        {
            throw new NotFoundException("Doctor", doctorId);
        }

        return await LoadScheduleAsync(doctorId, activeOnly: true, ct);
    }

    public Task<IReadOnlyList<WorkingScheduleItemDto>> GetMyScheduleAsync(CancellationToken ct) =>
        LoadScheduleAsync(RequireDoctorId(), activeOnly: false, ct);

    public async Task<IReadOnlyList<WorkingScheduleItemDto>> ReplaceMyScheduleAsync(SaveScheduleRequest request, CancellationToken ct)
    {
        var doctorId = RequireDoctorId();
        ScheduleValidator.Validate(request.Items);

        var existing = await _db.WorkingSchedules.Where(w => w.DoctorId == doctorId).ToListAsync(ct);
        _db.WorkingSchedules.RemoveRange(existing);
        _db.WorkingSchedules.AddRange(request.Items.Select(i => new WorkingSchedule
        {
            DoctorId = doctorId,
            DayOfWeek = i.DayOfWeek,
            StartTime = i.StartTime,
            EndTime = i.EndTime,
            SlotDurationMinutes = i.SlotDurationMinutes,
            IsActive = i.IsActive
        }));

        // SaveChanges واحدة = حذف القديم وإضافة الجديد في Transaction واحدة
        await _db.SaveChangesAsync(ct);

        return await LoadScheduleAsync(doctorId, activeOnly: false, ct);
    }

    // ------------------------------------------------------------------ الإجازات

    public async Task<IReadOnlyList<TimeOffDto>> ListMyTimeOffsAsync(bool upcomingOnly, CancellationToken ct)
    {
        var doctorId = RequireDoctorId();
        var query = _db.TimeOffs.AsNoTracking().Where(t => t.DoctorId == doctorId);

        if (upcomingOnly)
        {
            var now = _time.GetUtcNow().UtcDateTime;
            query = query.Where(t => t.EndAt > now);
        }

        var rows = await query.OrderBy(t => t.StartAt).ToListAsync(ct);
        return rows.Select(ToDto).ToList();
    }

    public async Task<TimeOffDto> CreateMyTimeOffAsync(CreateTimeOffRequest request, CancellationToken ct)
    {
        var doctorId = RequireDoctorId();
        var start = request.StartAt!.Value.UtcDateTime;
        var end = request.EndAt!.Value.UtcDateTime;
        var now = _time.GetUtcNow().UtcDateTime;

        var errors = new List<string>();
        if (end <= start)
        {
            errors.Add("endAt must be after startAt.");
        }
        if (end <= now)
        {
            errors.Add("The time off period must end in the future.");
        }
        if (errors.Count > 0)
        {
            throw new AppValidationException(new Dictionary<string, string[]> { ["timeOff"] = errors.ToArray() });
        }

        if (await _db.TimeOffs.AnyAsync(t => t.DoctorId == doctorId && t.StartAt < end && t.EndAt > start, ct))
        {
            throw new ConflictException("This period overlaps with another time off.");
        }

        // مينفعش إجازة تغطي مواعيد محجوزة، لازم تتلغي أو تتنقل الأول
        var activeStatuses = new[] { AppointmentStatus.PendingPayment, AppointmentStatus.Confirmed, AppointmentStatus.CheckedIn };
        if (await _db.Appointments.AnyAsync(
                a => a.DoctorId == doctorId && activeStatuses.Contains(a.Status) && a.StartAt < end && a.EndAt > start, ct))
        {
            throw new ConflictException("There are booked appointments in this period. Cancel or move them first.");
        }

        var timeOff = new TimeOff
        {
            DoctorId = doctorId,
            StartAt = start,
            EndAt = end,
            Reason = string.IsNullOrWhiteSpace(request.Reason) ? null : request.Reason.Trim()
        };
        _db.TimeOffs.Add(timeOff);
        await _db.SaveChangesAsync(ct);
        return ToDto(timeOff);
    }

    public async Task DeleteMyTimeOffAsync(int id, CancellationToken ct)
    {
        var doctorId = RequireDoctorId();

        // إجازة دكتور تاني بترجع 404 (مش 403) عشان ما نكشفش إنها موجودة
        var timeOff = await _db.TimeOffs.FirstOrDefaultAsync(t => t.Id == id && t.DoctorId == doctorId, ct)
            ?? throw new NotFoundException("TimeOff", id);

        _db.TimeOffs.Remove(timeOff);
        await _db.SaveChangesAsync(ct);
    }

    // ------------------------------------------------------------------ مساعدات

    private int RequireDoctorId() =>
        _currentUser.DoctorId ?? throw new ForbiddenException("This account is not linked to a doctor profile.");

    private async Task<IReadOnlyList<WorkingScheduleItemDto>> LoadScheduleAsync(int doctorId, bool activeOnly, CancellationToken ct)
    {
        var query = _db.WorkingSchedules.AsNoTracking().Where(w => w.DoctorId == doctorId);
        if (activeOnly)
        {
            query = query.Where(w => w.IsActive);
        }

        var rows = await query.ToListAsync(ct);
        return rows
            .OrderBy(w => w.DayOfWeek).ThenBy(w => w.StartTime)
            .Select(w => new WorkingScheduleItemDto
            {
                DayOfWeek = w.DayOfWeek,
                StartTime = w.StartTime,
                EndTime = w.EndTime,
                SlotDurationMinutes = w.SlotDurationMinutes,
                IsActive = w.IsActive
            })
            .ToList();
    }

    // EF بيرجّع DateTime من غير Kind، فبنحدد إنه UTC عشان الـ JSON يطلع بـ Z
    private static TimeOffDto ToDto(TimeOff t) => new()
    {
        Id = t.Id,
        StartAt = DateTime.SpecifyKind(t.StartAt, DateTimeKind.Utc),
        EndAt = DateTime.SpecifyKind(t.EndAt, DateTimeKind.Utc),
        Reason = t.Reason
    };
}

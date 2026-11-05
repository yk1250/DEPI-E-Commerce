using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Clinic.Application.Common.Exceptions;
using Clinic.Application.Common.Interfaces;
using Clinic.Application.Common.Time;
using Clinic.Application.Features.Waitlist;
using Clinic.Domain.Entities;
using Clinic.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace Clinic.Application.Features.Appointments;

public class AppointmentService
{
    private static readonly TimeSpan PaymentWindow = TimeSpan.FromMinutes(15);
    private static readonly TimeSpan CancelCutoff = TimeSpan.FromHours(2);

    private readonly IAppDbContext _db;
    private readonly TimeProvider _time;
    private readonly WaitlistService? _waitlist;

    public AppointmentService(IAppDbContext db, TimeProvider? time = null, WaitlistService? waitlist = null)
    {
        _db = db;
        _time = time ?? TimeProvider.System;
        _waitlist = waitlist;
    }

    public async Task<Appointment> BookAsync(BookAppointmentRequest request, CancellationToken ct = default)
    {
        var nowUtc = _time.GetUtcNow().UtcDateTime;

        // 0) مينفعش نحجز ميعاد بدأ أو فات
        if (request.StartAtUtc <= nowUtc)
        {
            throw new AppValidationException("Cannot book an appointment that has already started or passed.");
        }

        var doctor = await _db.Doctors
            .AsNoTracking()
            .FirstOrDefaultAsync(d => d.Id == request.DoctorId, ct)
            ?? throw new NotFoundException(nameof(Doctor), request.DoctorId);

        // 0a) الدكتور لازم يكون معتمد من الإدارة
        if (!doctor.IsApproved)
        {
            throw new AppValidationException("This doctor is not approved for bookings yet.");
        }

        // 0b) حساب الدكتور لازم يكون شغال
        var doctorAccount = await _db.UserSummaries
            .AsNoTracking()
            .FirstOrDefaultAsync(u => u.Id == doctor.UserId, ct);

        if (doctorAccount is null || !doctorAccount.IsActive)
        {
            throw new AppValidationException("This doctor's account is not active.");
        }

        // 1) نحوّل الوقت لتوقيت القاهرة
        var localStart = ClinicTime.ToLocal(request.StartAtUtc);
        var localDate = DateOnly.FromDateTime(localStart);
        var localTime = TimeOnly.FromDateTime(localStart).ToTimeSpan();

        // 2) كل جداول الدكتور لليوم ده (ممكن تكون فترتين)
        var schedules = await _db.WorkingSchedules
            .AsNoTracking()
            .Where(w => w.DoctorId == request.DoctorId
                && w.IsActive
                && w.DayOfWeek == localDate.DayOfWeek)
            .ToListAsync(ct);

        if (schedules.Count == 0)
        {
            throw new AppValidationException(
                $"The doctor does not work on {localDate.DayOfWeek}. Requested local time: {localStart:HH:mm}.");
        }

        // 3) نلاقي الفترة اللي الموعد جواها
        var containing = schedules
            .Where(s => localTime >= s.StartTime.ToTimeSpan()
                && localTime + TimeSpan.FromMinutes(s.SlotDurationMinutes) <= s.EndTime.ToTimeSpan())
            .ToList();

        if (containing.Count == 0)
        {
            var shifts = string.Join(", ", schedules.Select(s => $"{s.StartTime:HH:mm}-{s.EndTime:HH:mm}"));
            throw new AppValidationException(
                $"The requested time is outside the doctor's working hours. Requested local time: {localStart:HH:mm}. Shifts: {shifts}.");
        }

        // 4) الموعد لازم يكون على شبكة الكشف بتاعة الفترة
        var schedule = containing.FirstOrDefault(s =>
            (localTime - s.StartTime.ToTimeSpan()).TotalMinutes % s.SlotDurationMinutes == 0);

        if (schedule is null)
        {
            throw new AppValidationException("The requested time does not match the slot grid.");
        }

        var slotLength = TimeSpan.FromMinutes(schedule.SlotDurationMinutes);

        // 5) التعارض مع حجز تاني
        var endAtUtc = request.StartAtUtc.Add(slotLength);

        var isTaken = await _db.Appointments.AnyAsync(a =>
            a.DoctorId == request.DoctorId
            && a.StartAt == request.StartAtUtc
            && a.Status != AppointmentStatus.Cancelled
            && a.Status != AppointmentStatus.Expired, ct);

        if (isTaken)
        {
            throw new ConflictException("This slot is already booked.");
        }

        // 6) قواعد المتابعة: لو الحجز FollowUp ولقينا كشف مكتمل قريب، نطبّق الخصم.
        //    غير كده بيتحوّل لكشف عادي بالسعر الكامل.
        var appointmentType = request.Type;
        var price = doctor.ConsultationFee;
        int? previousId = null;

        if (request.Type == AppointmentType.FollowUp)
        {
            var previous = await _db.Appointments
                .AsNoTracking()
                .Where(a => a.PatientId == request.PatientId
                    && a.DoctorId == request.DoctorId
                    && a.Status == AppointmentStatus.Completed
                    && a.EndAt <= request.StartAtUtc)
                .OrderByDescending(a => a.EndAt)
                .FirstOrDefaultAsync(ct);

            var windowEnd = previous?.EndAt.AddDays(doctor.FollowUpWindowDays);

            if (previous is not null && request.StartAtUtc <= windowEnd)
            {
                previousId = previous.Id;
                price = doctor.ConsultationFee * doctor.FollowUpFeePercent / 100m;
            }
            else
            {
                appointmentType = AppointmentType.New;
            }
        }

        var isOnline = request.PaymentMethod == PaymentMethod.Online;

        var appointment = new Appointment
        {
            DoctorId = request.DoctorId,
            PatientId = request.PatientId,
            StartAt = request.StartAtUtc,
            EndAt = endAtUtc,
            Type = appointmentType,
            PreviousAppointmentId = previousId,
            Status = isOnline ? AppointmentStatus.PendingPayment : AppointmentStatus.Confirmed,
            PriceSnapshot = price,
            ExpiresAt = isOnline ? nowUtc.Add(PaymentWindow) : null,
            CreatedAt = nowUtc
        };

        _db.Appointments.Add(appointment);

        try
        {
            await _db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException)
        {
            // الـ Unique Index رفض الحفظ: مريض تاني حجز نفس الميعاد في نفس اللحظة
            throw new ConflictException("This slot is already booked.");
        }

        return appointment;
    }

    /// المريض يلغي موعده. بيفتح الميعاد للحجز تاني، وبيبلّغ أول مريض في قايمة الانتظار.
    /// ممنوع قبل الموعد بأقل من ساعتين.
    public async Task<Appointment> CancelByPatientAsync(
        int appointmentId,
        int patientId,
        string? reason,
        CancellationToken ct = default)
    {
        var appointment = await LoadAsync(appointmentId, ct);

        if (appointment.PatientId != patientId)
        {
            throw new ForbiddenException("You can only cancel your own appointments.");
        }

        if (appointment.Status is not (AppointmentStatus.PendingPayment or AppointmentStatus.Confirmed))
        {
            throw new ConflictException($"An appointment with status {appointment.Status} cannot be cancelled.");
        }

        var nowUtc = _time.GetUtcNow().UtcDateTime;

        if (appointment.StartAt - nowUtc < CancelCutoff)
        {
            throw new ConflictException("Appointments can only be cancelled at least 2 hours before the start time.");
        }

        appointment.Status = AppointmentStatus.Cancelled;
        appointment.CancelledAt = nowUtc;
        appointment.CancelReason = reason;

        await _db.SaveChangesAsync(ct);

        await NotifyWaitlistAsync(appointment, ct);

        return appointment;
    }

    /// الدكتور صاحب الموعد يلغي موعده، وبيبلّغ أول مريض في قايمة الانتظار.
    /// مفيش قاعدة ساعتين للدكتور. من PendingPayment أو Confirmed بس.
    public async Task<Appointment> CancelByDoctorAsync(
        int appointmentId,
        int doctorId,
        string? reason,
        CancellationToken ct = default)
    {
        var appointment = await LoadAsync(appointmentId, ct);

        if (appointment.DoctorId != doctorId)
        {
            throw new ForbiddenException("You can only cancel your own appointments.");
        }

        if (appointment.Status is not (AppointmentStatus.PendingPayment or AppointmentStatus.Confirmed))
        {
            throw new ConflictException($"An appointment with status {appointment.Status} cannot be cancelled.");
        }

        var nowUtc = _time.GetUtcNow().UtcDateTime;

        appointment.Status = AppointmentStatus.Cancelled;
        appointment.CancelledAt = nowUtc;
        appointment.CancelReason = reason;

        await _db.SaveChangesAsync(ct);

        await NotifyWaitlistAsync(appointment, ct);

        return appointment;
    }

    /// الريسبشن أو الدكتور: المريض وصل العيادة. من Confirmed بس.
    public async Task<Appointment> CheckInAsync(int appointmentId, CancellationToken ct = default)
    {
        var appointment = await LoadAsync(appointmentId, ct);

        if (appointment.Status != AppointmentStatus.Confirmed)
        {
            throw new ConflictException($"Only a Confirmed appointment can be checked in. Current status: {appointment.Status}.");
        }

        appointment.Status = AppointmentStatus.CheckedIn;

        await _db.SaveChangesAsync(ct);

        return appointment;
    }

    /// الدكتور: خلص الكشف. من CheckedIn بس.
    public async Task<Appointment> CompleteAsync(int appointmentId, CancellationToken ct = default)
    {
        var appointment = await LoadAsync(appointmentId, ct);

        if (appointment.Status != AppointmentStatus.CheckedIn)
        {
            throw new ConflictException($"Only a CheckedIn appointment can be completed. Current status: {appointment.Status}.");
        }

        appointment.Status = AppointmentStatus.Completed;

        await _db.SaveChangesAsync(ct);

        return appointment;
    }

    /// الدكتور: المريض ما جاش. من Confirmed بس، وبعد ما الموعد يبدأ.
    public async Task<Appointment> MarkNoShowAsync(int appointmentId, CancellationToken ct = default)
    {
        var appointment = await LoadAsync(appointmentId, ct);

        if (appointment.Status != AppointmentStatus.Confirmed)
        {
            throw new ConflictException($"Only a Confirmed appointment can be marked as no-show. Current status: {appointment.Status}.");
        }

        var nowUtc = _time.GetUtcNow().UtcDateTime;

        if (nowUtc < appointment.StartAt)
        {
            throw new ConflictException("A no-show can only be marked after the appointment start time.");
        }

        appointment.Status = AppointmentStatus.NoShow;

        await _db.SaveChangesAsync(ct);

        return appointment;
    }

    /// بيرجّع الدكتور والعيادة بتاعة الموعد، عشان الـ Controller يتحقق من الصلاحيات.
    public async Task<(int DoctorId, int ClinicId)> GetClinicScopeAsync(int appointmentId, CancellationToken ct = default)
    {
        var scope = await (from a in _db.Appointments
                           join d in _db.Doctors on a.DoctorId equals d.Id
                           where a.Id == appointmentId
                           select new { a.DoctorId, d.ClinicId })
            .FirstOrDefaultAsync(ct)
            ?? throw new NotFoundException(nameof(Appointment), appointmentId);

        return (scope.DoctorId, scope.ClinicId);
    }

    /// يحوّل الحجوزات PendingPayment اللي انتهت مهلة دفعها لـ Expired، ويرجّع عددها.
    /// الـ Hosted Service بيستدعيه دورياً. الـ Confirmed ما بيتلمسش أبداً.
    public async Task<int> ExpirePendingAsync(CancellationToken ct = default)
    {
        var nowUtc = _time.GetUtcNow().UtcDateTime;

        var expired = await _db.Appointments
            .Where(a => a.Status == AppointmentStatus.PendingPayment
                && a.ExpiresAt != null
                && a.ExpiresAt < nowUtc)
            .ToListAsync(ct);

        foreach (var appointment in expired)
        {
            appointment.Status = AppointmentStatus.Expired;
        }

        if (expired.Count > 0)
        {
            await _db.SaveChangesAsync(ct);
        }

        return expired.Count;
    }

    /// لو الـ WaitlistService متسجل، بنبلّغ أول مريض في قايمة اليوم اللي الموعد اتفتح فيه (بتوقيت القاهرة).
    private async Task NotifyWaitlistAsync(Appointment appointment, CancellationToken ct)
    {
        if (_waitlist is null)
        {
            return;
        }

        var localDate = DateOnly.FromDateTime(ClinicTime.ToLocal(appointment.StartAt));

        await _waitlist.NotifyFirstForOpenSlotAsync(appointment.DoctorId, localDate, ct);
    }

    private async Task<Appointment> LoadAsync(int appointmentId, CancellationToken ct)
    {
        return await _db.Appointments
            .FirstOrDefaultAsync(a => a.Id == appointmentId, ct)
            ?? throw new NotFoundException(nameof(Appointment), appointmentId);
    }
}
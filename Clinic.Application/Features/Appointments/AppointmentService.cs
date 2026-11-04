using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Clinic.Application.Common.Exceptions;
using Clinic.Application.Common.Interfaces;
using Clinic.Application.Common.Time;
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

    public AppointmentService(IAppDbContext db, TimeProvider? time = null)
    {
        _db = db;
        _time = time ?? TimeProvider.System;
    }

    public async Task<Appointment> BookAsync(BookAppointmentRequest request, CancellationToken ct = default)
    {
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

        var nowUtc = _time.GetUtcNow().UtcDateTime;
        var isOnline = request.PaymentMethod == PaymentMethod.Online;

        var appointment = new Appointment
        {
            DoctorId = request.DoctorId,
            PatientId = request.PatientId,
            StartAt = request.StartAtUtc,
            EndAt = endAtUtc,
            Type = request.Type,
            Status = isOnline ? AppointmentStatus.PendingPayment : AppointmentStatus.Confirmed,
            PriceSnapshot = doctor.ConsultationFee,
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

    /// المريض يلغي موعده. بيفتح الميعاد للحجز تاني. ممنوع قبل الموعد بأقل من ساعتين.
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

    private async Task<Appointment> LoadAsync(int appointmentId, CancellationToken ct)
    {
        return await _db.Appointments
            .FirstOrDefaultAsync(a => a.Id == appointmentId, ct)
            ?? throw new NotFoundException(nameof(Appointment), appointmentId);
    }
}
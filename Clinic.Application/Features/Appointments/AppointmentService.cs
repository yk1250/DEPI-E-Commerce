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

    private readonly IAppDbContext _db;

    public AppointmentService(IAppDbContext db)
    {
        _db = db;
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
            ExpiresAt = isOnline ? DateTime.UtcNow.Add(PaymentWindow) : null,
            CreatedAt = DateTime.UtcNow
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

    /// المريض يلغي موعده. بيفتح الميعاد للحجز تاني.
    public async Task<Appointment> CancelByPatientAsync(
        int appointmentId,
        int patientId,
        string? reason,
        CancellationToken ct = default)
    {
        // tracked عشان نعدّل عليه ونحفظ
        var appointment = await _db.Appointments
            .FirstOrDefaultAsync(a => a.Id == appointmentId, ct)
            ?? throw new NotFoundException(nameof(Appointment), appointmentId);

        if (appointment.PatientId != patientId)
        {
            throw new ForbiddenException("You can only cancel your own appointments.");
        }

        if (appointment.Status is not (AppointmentStatus.PendingPayment or AppointmentStatus.Confirmed))
        {
            throw new ConflictException($"An appointment with status {appointment.Status} cannot be cancelled.");
        }

        appointment.Status = AppointmentStatus.Cancelled;
        appointment.CancelledAt = DateTime.UtcNow;
        appointment.CancelReason = reason;

        await _db.SaveChangesAsync(ct);

        return appointment;
    }
}
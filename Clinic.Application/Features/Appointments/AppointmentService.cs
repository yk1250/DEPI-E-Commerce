using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Clinic.Application.Common.Exceptions;
using Clinic.Application.Common.Interfaces;
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

        var endAtUtc = request.StartAtUtc.AddMinutes(30);

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
}
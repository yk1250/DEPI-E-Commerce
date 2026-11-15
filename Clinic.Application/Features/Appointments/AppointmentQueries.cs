using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Clinic.Application.Common.Interfaces;
using Clinic.Application.Common.Time;
using Clinic.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Clinic.Application.Features.Appointments;

public class AppointmentQueries
{
    private readonly IAppDbContext _db;

    public AppointmentQueries(IAppDbContext db)
    {
        _db = db;
    }

    /// مواعيد المريض فقط، مرتبة من الأقرب للأبعد.
    public async Task<IReadOnlyList<Appointment>> GetPatientAppointmentsAsync(
        int patientId,
        CancellationToken ct = default)
    {
        return await _db.Appointments
            .AsNoTracking()
            .Where(a => a.PatientId == patientId)
            .OrderBy(a => a.StartAt)
            .ToListAsync(ct);
    }

    /// مواعيد الدكتور في يوم معين بتوقيت القاهرة (يوم الدكتور مش يوم UTC).
    public async Task<IReadOnlyList<Appointment>> GetDoctorAppointmentsAsync(
        int doctorId,
        DateOnly date,
        CancellationToken ct = default)
    {
        // بداية اليوم بتوقيت القاهرة، محوّلة لـ UTC عشان نقارنها بالداتابيز
        var dayStartUtc = ClinicTime.ToUtc(date, TimeOnly.MinValue);
        var dayEndUtc = ClinicTime.ToUtc(date.AddDays(1), TimeOnly.MinValue);

        return await _db.Appointments
            .AsNoTracking()
            .Where(a => a.DoctorId == doctorId
                && a.StartAt >= dayStartUtc
                && a.StartAt < dayEndUtc)
            .OrderBy(a => a.StartAt)
            .ToListAsync(ct);
    }
}
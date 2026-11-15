using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Clinic.Application.Common.Interfaces;
using Clinic.Application.Common.Time;
using Clinic.Application.Features.Slots;
using Clinic.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace Clinic.Infrastructure.Booking;

public class DoctorAvailabilityReader : IDoctorAvailabilityReader
{
    private readonly IAppDbContext _db;

    public DoctorAvailabilityReader(IAppDbContext db)
    {
        _db = db;
    }

    public async Task<DoctorAvailability?> GetAsync(int doctorId, DateOnly date, CancellationToken ct = default)
    {
        // 1) الدوام: بتوقيت القاهرة، فمفيش تحويل
        var schedule = await _db.WorkingSchedules
            .AsNoTracking()
            .Where(w => w.DoctorId == doctorId && w.IsActive && w.DayOfWeek == date.DayOfWeek)
            .OrderBy(w => w.StartTime)
            .FirstOrDefaultAsync(ct);

        if (schedule is null)
        {
            return null;
        }

        // حدود اليوم بالـ UTC (زي ما الداتابيز مخزنة)
        var dayStartUtc = ClinicTime.ToUtc(date, TimeOnly.MinValue);
        var dayEndUtc = ClinicTime.ToUtc(date.AddDays(1), TimeOnly.MinValue);

        // 2) المواعيد المحجوزة: بنستبعد الملغي والمنتهي بس
        var bookedUtc = await _db.Appointments
            .AsNoTracking()
            .Where(a => a.DoctorId == doctorId
                && a.StartAt >= dayStartUtc
                && a.StartAt < dayEndUtc
                && a.Status != AppointmentStatus.Cancelled
                && a.Status != AppointmentStatus.Expired)
            .Select(a => a.StartAt)
            .ToListAsync(ct);

        var bookedStarts = bookedUtc
            .Select(s => TimeOnly.FromDateTime(ClinicTime.ToLocal(s)))
            .ToList();

        // 3) الإجازات اللي بتقطع اليوم ده
        var timeOffsUtc = await _db.TimeOffs
            .AsNoTracking()
            .Where(t => t.DoctorId == doctorId
                && t.StartAt < dayEndUtc
                && t.EndAt > dayStartUtc)
            .Select(t => new { t.StartAt, t.EndAt })
            .ToListAsync(ct);

        var timeOffs = timeOffsUtc
            .Select(t => (
                From: t.StartAt < dayStartUtc
                    ? TimeOnly.MinValue
                    : TimeOnly.FromDateTime(ClinicTime.ToLocal(t.StartAt)),
                To: t.EndAt > dayEndUtc
                    ? TimeOnly.MaxValue
                    : TimeOnly.FromDateTime(ClinicTime.ToLocal(t.EndAt))))
            .ToList();

        return new DoctorAvailability(
            schedule.StartTime,
            schedule.EndTime,
            schedule.SlotDurationMinutes,
            bookedStarts,
            timeOffs);
    }
}
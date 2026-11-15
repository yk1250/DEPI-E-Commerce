using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Clinic.Application.Common.Exceptions;
using Clinic.Application.Common.Interfaces;
using Clinic.Domain.Entities;
using Clinic.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace Clinic.Application.Features.Waitlist;

public class WaitlistService
{
    private readonly IAppDbContext _db;
    private readonly IEmailSender _email;

    public WaitlistService(IAppDbContext db, IEmailSender email)
    {
        _db = db;
        _email = email;
    }

    /// المريض يسجّل نفسه في قايمة انتظار دكتور ويوم معين.
    public async Task<WaitlistEntry> JoinAsync(
        int patientId,
        int doctorId,
        DateOnly desiredDate,
        CancellationToken ct = default)
    {
        var exists = await _db.WaitlistEntries.AnyAsync(w =>
            w.PatientId == patientId
            && w.DoctorId == doctorId
            && w.DesiredDate == desiredDate
            && w.IsActive, ct);

        if (exists)
        {
            throw new ConflictException("You are already on the waitlist for this doctor and date.");
        }

        var entry = new WaitlistEntry
        {
            PatientId = patientId,
            DoctorId = doctorId,
            DesiredDate = desiredDate,
            IsActive = true
        };

        _db.WaitlistEntries.Add(entry);
        await _db.SaveChangesAsync(ct);

        return entry;
    }

    /// ميعاد اتفتح لدكتور في يوم معين: بنبلّغ أول مريض في القايمة بس.
    /// بيرجّع عدد المرضى اللي اتبلغوا (0 أو 1).
    public async Task<int> NotifyFirstForOpenSlotAsync(
        int doctorId,
        DateOnly date,
        CancellationToken ct = default)
    {
        var first = await _db.WaitlistEntries
            .Where(w => w.DoctorId == doctorId
                && w.DesiredDate == date
                && w.IsActive
                && w.NotifiedAt == null)
            .OrderBy(w => w.Id)
            .FirstOrDefaultAsync(ct);

        if (first is null)
        {
            return 0;
        }

        var patient = await _db.Patients
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.Id == first.PatientId, ct);

        if (patient is null)
        {
            return 0;
        }

        var account = await _db.UserSummaries
            .AsNoTracking()
            .FirstOrDefaultAsync(u => u.Id == patient.UserId, ct);

        if (account is null)
        {
            return 0;
        }

        var title = "ميعاد اتفتح";
        var body = $"اتفتح ميعاد يوم {date:yyyy-MM-dd}. احجز بسرعة قبل ما يتحجز.";

        _db.Notifications.Add(new Notification
        {
            UserId = patient.UserId,
            Type = NotificationType.Waitlist,
            Channel = NotificationChannel.Email,
            Title = title,
            Body = body,
            SentAt = null
        });

        await _email.SendAsync(account.Email ?? string.Empty, title, body, ct);

        first.NotifiedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);

        return 1;
    }
}
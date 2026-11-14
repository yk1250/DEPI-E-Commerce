using Clinic.Application.Common.Exceptions;
using Clinic.Application.Common.Interfaces;
using Clinic.Domain.Entities;
using Clinic.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace Clinic.Application.Features.Payments;

/// التنفيذ الافتراضي لتأكيد الموعد بعد الدفع الأونلاين (ممكن يتبدّل بنسخة الشخص 2).
/// الـ RowVersion بيحمي من تعارض مع مهمة انتهاء المواعيد: لو اتغيّر الموعد في نفس اللحظة بيرمي
/// DbUpdateConcurrencyException، والإشعار بيرجّع 500 وPaymob بيبعته تاني.
public sealed class AppointmentConfirmer : IAppointmentConfirmer
{
    private readonly IAppDbContext _db;

    public AppointmentConfirmer(IAppDbContext db)
    {
        _db = db;
    }

    public async Task ConfirmAsync(int appointmentId, CancellationToken ct = default)
    {
        var appointment = await _db.Appointments.FirstOrDefaultAsync(a => a.Id == appointmentId, ct)
            ?? throw new NotFoundException(nameof(Appointment), appointmentId);

        if (appointment.Status is AppointmentStatus.Confirmed
            or AppointmentStatus.CheckedIn
            or AppointmentStatus.Completed)
        {
            return;
        }

        if (appointment.Status != AppointmentStatus.PendingPayment)
        {
            throw new ConflictException(
                $"The appointment cannot be confirmed because its status is {appointment.Status}.");
        }

        appointment.Status = AppointmentStatus.Confirmed;
        appointment.ExpiresAt = null;
        await _db.SaveChangesAsync(ct);
    }
}

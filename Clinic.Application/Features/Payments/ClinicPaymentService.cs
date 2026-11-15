using Clinic.Application.Common.Exceptions;
using Clinic.Application.Common.Interfaces;
using Clinic.Application.Common.Security;
using Clinic.Application.Features.Coupons;
using Clinic.Domain.Entities;
using Clinic.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace Clinic.Application.Features.Payments;

/// الدفع في العيادة: الريسبشن بيأكد إن المريض دفع كاش، فبيتعمل صف Payment بحالة Paid.
/// - الريسبشن شغله جوه عيادته بس.
/// - الموعد لازم يكون Confirmed أو CheckedIn.
/// - الكوبون (لو موجود) بيتستهلك في نفس الـ transaction مع حفظ الدفع.
public class ClinicPaymentService
{
    private readonly IAppDbContext _db;
    private readonly CouponService _coupons;
    private readonly ICurrentUser _currentUser;
    private readonly TimeProvider _time;

    public ClinicPaymentService(
        IAppDbContext db,
        CouponService coupons,
        ICurrentUser currentUser,
        TimeProvider time)
    {
        _db = db;
        _coupons = coupons;
        _currentUser = currentUser;
        _time = time;
    }

    public async Task<PaymentDto> ConfirmAsync(
        int appointmentId,
        string? couponCode,
        CancellationToken ct = default)
    {
        var clinicId = RequireReceptionistClinic();

        var appointment = await (from a in _db.Appointments.AsNoTracking()
                                 join d in _db.Doctors.AsNoTracking() on a.DoctorId equals d.Id
                                 where a.Id == appointmentId
                                 select new { a.Status, a.PriceSnapshot, d.ClinicId })
            .FirstOrDefaultAsync(ct)
            ?? throw new NotFoundException(nameof(Appointment), appointmentId);

        if (appointment.ClinicId != clinicId)
        {
            throw new ForbiddenException("You can only manage appointments in your clinic.");
        }

        if (appointment.Status is not (AppointmentStatus.Confirmed or AppointmentStatus.CheckedIn))
        {
            throw new ConflictException(
                $"Payment can only be confirmed for a Confirmed or CheckedIn appointment. Current status: {appointment.Status}.");
        }

        // الموعد في العيادة ملوش صف دفع قبل التأكيد. أي صف موجود يبقى مدفوع أو فيه عملية تانية، فنرفض.
        var existing = await _db.Payments
            .AsNoTracking()
            .Where(p => p.AppointmentId == appointmentId)
            .Select(p => new { p.Status })
            .FirstOrDefaultAsync(ct);

        if (existing is not null)
        {
            throw new ConflictException(existing.Status == PaymentStatus.Paid
                ? "This appointment is already paid."
                : $"This appointment already has a payment record (status: {existing.Status}).");
        }

        var original = appointment.PriceSnapshot;
        var discount = 0m;
        Coupon? coupon = null;

        if (!string.IsNullOrWhiteSpace(couponCode))
        {
            coupon = await _coupons.GetUsableCouponAsync(couponCode, ct);
            discount = CouponCalculator.Discount(coupon.DiscountType, coupon.DiscountValue, original);
        }

        var now = _time.GetUtcNow().UtcDateTime;
        var payment = new Payment
        {
            AppointmentId = appointmentId,
            CouponId = coupon?.Id,
            Method = PaymentMethod.AtClinic,
            Status = PaymentStatus.Paid,
            OriginalAmount = original,
            DiscountAmount = discount,
            Amount = CouponCalculator.FinalAmount(original, discount),
            PaidAt = now,
            CreatedAt = now
        };

        // استهلاك الكوبون وحفظ الدفع: يا الاتنين يا ولا واحد
        await using var transaction = await _db.Database.BeginTransactionAsync(ct);
        try
        {
            if (coupon is not null)
            {
                await _coupons.RedeemAsync(coupon.Id, ct);
            }

            _db.Payments.Add(payment);
            await _db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
        }
        catch (DbUpdateException)
        {
            // الـ Unique Index على AppointmentId: ريسبشن تاني أكد نفس الموعد في نفس اللحظة
            throw new ConflictException("This appointment is already paid.");
        }

        return new PaymentDto(
            payment.Id,
            payment.AppointmentId,
            payment.Method,
            payment.Status,
            payment.OriginalAmount,
            payment.DiscountAmount,
            payment.Amount,
            coupon?.Code,
            DateTime.SpecifyKind(now, DateTimeKind.Utc));
    }

    private int RequireReceptionistClinic()
    {
        if (!_currentUser.IsAuthenticated)
        {
            throw new UnauthorizedException();
        }

        if (_currentUser.Role != Roles.Receptionist || _currentUser.ClinicId is not int clinicId)
        {
            throw new ForbiddenException("Only receptionists can confirm clinic payments.");
        }

        return clinicId;
    }
}

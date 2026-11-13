using Clinic.Application.Common.Exceptions;
using Clinic.Application.Common.Interfaces;
using Clinic.Application.Common.Security;
using Clinic.Application.Features.Coupons;
using Clinic.Domain.Entities;
using Clinic.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace Clinic.Application.Features.Payments;

/// بدء الدفع الأونلاين: المريض بيطلب رابط صفحة دفع Paymob لموعد بتاعه مستني الدفع.
/// - بيتعمل صف Payment بحالة Pending (أو بيتعاد استخدامه لو المحاولة السابقة ما كملتش).
/// - الكوبون بيتحسب خصمه هنا بس مش بيتستهلك: الاستهلاك بيحصل بعد نجاح الدفع (الـ webhook).
/// - مدة صلاحية صفحة الدفع مش بتعدّي الوقت المتبقي للموعد (15 دقيقة من الحجز).
public class OnlinePaymentService
{
    private const int MinExpirationSeconds = 60;
    private const int MaxExpirationSeconds = 3600;

    private readonly IAppDbContext _db;
    private readonly CouponService _coupons;
    private readonly IPaymobClient _paymob;
    private readonly ICurrentUser _currentUser;
    private readonly TimeProvider _time;

    public OnlinePaymentService(
        IAppDbContext db,
        CouponService coupons,
        IPaymobClient paymob,
        ICurrentUser currentUser,
        TimeProvider time)
    {
        _db = db;
        _coupons = coupons;
        _paymob = paymob;
        _currentUser = currentUser;
        _time = time;
    }

    public async Task<OnlinePaymentDto> InitiateAsync(
        int appointmentId,
        string? couponCode,
        CancellationToken ct = default)
    {
        var patientId = RequireOwnPatientId();
        var now = _time.GetUtcNow().UtcDateTime;

        // موعد مريض تاني بيتعامل كأنه مش موجود
        var appointment = await _db.Appointments
            .AsNoTracking()
            .Where(a => a.Id == appointmentId && a.PatientId == patientId)
            .Select(a => new { a.Status, a.PriceSnapshot, a.ExpiresAt })
            .FirstOrDefaultAsync(ct)
            ?? throw new NotFoundException(nameof(Appointment), appointmentId);

        if (appointment.Status != AppointmentStatus.PendingPayment)
        {
            throw new ConflictException(
                $"Online payment is only available for appointments awaiting payment. Current status: {appointment.Status}.");
        }

        if (appointment.ExpiresAt is not DateTime expiresAt
            || expiresAt - now < TimeSpan.FromSeconds(MinExpirationSeconds))
        {
            throw new ConflictException("The payment window for this appointment has expired. Please book again.");
        }

        var payment = await _db.Payments.FirstOrDefaultAsync(p => p.AppointmentId == appointmentId, ct);
        if (payment is not null
            && (payment.Method != PaymentMethod.Online
                || payment.Status is PaymentStatus.Paid or PaymentStatus.Refunded))
        {
            throw new ConflictException(payment.Status == PaymentStatus.Paid
                ? "This appointment is already paid."
                : $"This appointment already has a payment record (status: {payment.Status}).");
        }

        var original = appointment.PriceSnapshot;
        var discount = 0m;
        Coupon? coupon = null;

        if (!string.IsNullOrWhiteSpace(couponCode))
        {
            coupon = await _coupons.GetUsableCouponAsync(couponCode, ct);
            discount = CouponCalculator.Discount(coupon.DiscountType, coupon.DiscountValue, original);
        }

        var amount = CouponCalculator.FinalAmount(original, discount);
        if (amount <= 0m)
        {
            throw new AppValidationException("The final amount is zero, so there is nothing to pay online.");
        }

        var amountCents = (long)Math.Round(amount * 100m, MidpointRounding.AwayFromZero);
        var expirationSeconds = (int)Math.Min(MaxExpirationSeconds, (expiresAt - now).TotalSeconds);

        // رقم الأوردر عندنا: A + رقم الموعد + جزء عشوائي (Paymob بيرفض تكرار الرقم)
        var checkout = await _paymob.CreateCheckoutAsync(
            new PaymobCheckoutRequest(
                amountCents,
                $"A{appointmentId}-{Guid.NewGuid():N}",
                expirationSeconds,
                new PaymobBilling("Patient", $"P{patientId}", $"patient{patientId}@clinic.local", "NA")),
            ct);

        if (payment is null)
        {
            payment = new Payment { AppointmentId = appointmentId, CreatedAt = now };
            _db.Payments.Add(payment);
        }

        payment.Method = PaymentMethod.Online;
        payment.Status = PaymentStatus.Pending;
        payment.CouponId = coupon?.Id;
        payment.OriginalAmount = original;
        payment.DiscountAmount = discount;
        payment.Amount = amount;
        payment.PaymobOrderId = checkout.OrderId;
        payment.TransactionId = null;
        payment.PaidAt = null;

        try
        {
            await _db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException)
        {
            // طلبين بدأوا الدفع لنفس الموعد في نفس اللحظة: الـ Unique Index على AppointmentId رفض التاني
            throw new ConflictException("A payment is already being started for this appointment. Please try again.");
        }

        return new OnlinePaymentDto(
            payment.Id,
            appointmentId,
            original,
            discount,
            amount,
            coupon?.Code,
            checkout.IframeUrl,
            DateTime.SpecifyKind(expiresAt, DateTimeKind.Utc));
    }

    private int RequireOwnPatientId()
    {
        if (!_currentUser.IsAuthenticated)
        {
            throw new UnauthorizedException();
        }

        if (_currentUser.Role != Roles.Patient || _currentUser.PatientId is not int patientId)
        {
            throw new ForbiddenException("Only patients can start an online payment.");
        }

        return patientId;
    }
}

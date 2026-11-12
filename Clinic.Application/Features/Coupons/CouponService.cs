using Clinic.Application.Common.Exceptions;
using Clinic.Application.Common.Interfaces;
using Clinic.Application.Common.Security;
using Clinic.Domain.Entities;
using Clinic.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace Clinic.Application.Features.Coupons;

/// الكوبونات: معاينة الخصم للمريض، وأدوات الدفع (GetUsableCouponAsync وRedeemAsync).
public class CouponService
{
    // رسالة واحدة لكل أسباب الرفض عشان محدّش يقدر يخمّن أكواد موجودة
    private const string InvalidCouponMessage = "This coupon is invalid, expired or fully used.";

    private readonly IAppDbContext _db;
    private readonly ICouponRedeemer _redeemer;
    private readonly ICurrentUser _currentUser;
    private readonly TimeProvider _time;

    public CouponService(
        IAppDbContext db,
        ICouponRedeemer redeemer,
        ICurrentUser currentUser,
        TimeProvider time)
    {
        _db = db;
        _redeemer = redeemer;
        _currentUser = currentUser;
        _time = time;
    }

    /// المريض يعاين الخصم على موعد بتاعه. مش بيستهلك الكوبون.
    public async Task<CouponValidationDto> ValidateAsync(
        ValidateCouponRequest request,
        CancellationToken ct = default)
    {
        var patientId = RequireOwnPatientId();

        // موعد مريض تاني بيتعامل كأنه مش موجود
        var appointment = await _db.Appointments
            .AsNoTracking()
            .Where(a => a.Id == request.AppointmentId && a.PatientId == patientId)
            .Select(a => new { a.PriceSnapshot, a.Status })
            .FirstOrDefaultAsync(ct);

        if (appointment is null)
        {
            throw new NotFoundException("Appointment was not found.");
        }

        if (appointment.Status is not (AppointmentStatus.PendingPayment
            or AppointmentStatus.Confirmed
            or AppointmentStatus.CheckedIn))
        {
            throw new ConflictException("This appointment is not awaiting payment.");
        }

        var alreadyPaid = await _db.Payments.AnyAsync(
            p => p.AppointmentId == request.AppointmentId && p.Status == PaymentStatus.Paid, ct);
        if (alreadyPaid)
        {
            throw new ConflictException("This appointment is already paid.");
        }

        var coupon = await GetUsableCouponAsync(request.Code, ct);

        var original = appointment.PriceSnapshot;
        var discount = CouponCalculator.Discount(coupon.DiscountType, coupon.DiscountValue, original);

        return new CouponValidationDto(
            coupon.Code,
            coupon.DiscountType,
            coupon.DiscountValue,
            original,
            discount,
            CouponCalculator.FinalAmount(original, discount));
    }

    /// بيرجّع الكوبون لو صالح دلوقتي (موجود، فعّال، ما انتهاش، وفيه استخدامات)، وإلا 400.
    /// خدمة الدفع بتستخدمها.
    public async Task<Coupon> GetUsableCouponAsync(string? code, CancellationToken ct = default)
    {
        var normalized = (code ?? string.Empty).Trim().ToUpperInvariant();
        if (normalized.Length == 0)
        {
            throw new AppValidationException(InvalidCouponMessage);
        }

        var coupon = await _db.Coupons
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.Code == normalized, ct);

        var now = _time.GetUtcNow().UtcDateTime;
        if (coupon is null || !coupon.IsActive || coupon.ExpiresAt <= now || coupon.UsedCount >= coupon.MaxUses)
        {
            throw new AppValidationException(InvalidCouponMessage);
        }

        return coupon;
    }

    /// يستهلك استخدام واحد بشكل ذري. لو آخر استخدام اتاخد في نفس اللحظة: 409.
    /// خدمة الدفع بتناديها وقت تأكيد الدفع.
    public async Task RedeemAsync(int couponId, CancellationToken ct = default)
    {
        var now = _time.GetUtcNow().UtcDateTime;
        if (!await _redeemer.TryRedeemAsync(couponId, now, ct))
        {
            throw new ConflictException("The coupon is no longer available.");
        }
    }

    private int RequireOwnPatientId()
    {
        if (!_currentUser.IsAuthenticated)
        {
            throw new UnauthorizedException();
        }

        if (_currentUser.Role != Roles.Patient || _currentUser.PatientId is not int patientId)
        {
            throw new ForbiddenException("Only patients can use coupons.");
        }

        return patientId;
    }
}

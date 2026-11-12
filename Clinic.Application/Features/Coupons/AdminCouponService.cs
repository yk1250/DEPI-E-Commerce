using System.Text.RegularExpressions;
using Clinic.Application.Common.Exceptions;
using Clinic.Application.Common.Interfaces;
using Clinic.Application.Common.Security;
using Clinic.Domain.Entities;
using Clinic.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace Clinic.Application.Features.Coupons;

/// إدارة الكوبونات للأدمن: قايمة وإنشاء وتعديل وحذف.
/// الكود ثابت بعد الإنشاء، وUsedCount بيزيد بس من الدفع. والكوبون اللي اتستخدم ما بيتحذفش (بيتعطّل).
public class AdminCouponService
{
    private const decimal MaxDiscountValue = 99_999_999.99m;
    private static readonly Regex CodePattern = new("^[A-Z0-9_-]{3,30}$", RegexOptions.Compiled);

    private readonly IAppDbContext _db;
    private readonly ICurrentUser _currentUser;
    private readonly TimeProvider _time;

    public AdminCouponService(IAppDbContext db, ICurrentUser currentUser, TimeProvider time)
    {
        _db = db;
        _currentUser = currentUser;
        _time = time;
    }

    public async Task<IReadOnlyList<CouponDto>> ListAsync(CancellationToken ct = default)
    {
        RequireAdmin();
        var now = Now();

        var coupons = await _db.Coupons.AsNoTracking().OrderByDescending(c => c.Id).ToListAsync(ct);
        return coupons.Select(c => ToDto(c, now)).ToList();
    }

    public async Task<CouponDto> GetAsync(int id, CancellationToken ct = default)
    {
        RequireAdmin();

        var coupon = await _db.Coupons.AsNoTracking().FirstOrDefaultAsync(c => c.Id == id, ct)
            ?? throw new NotFoundException(nameof(Coupon), id);

        return ToDto(coupon, Now());
    }

    public async Task<CouponDto> CreateAsync(CreateCouponRequest request, CancellationToken ct = default)
    {
        RequireAdmin();
        var now = Now();

        var code = (request.Code ?? string.Empty).Trim().ToUpperInvariant();
        if (!CodePattern.IsMatch(code))
        {
            throw Invalid("code", "Code must be 3-30 characters: letters, digits, '-' or '_'.");
        }

        var type = request.DiscountType
            ?? throw Invalid("discountType", "Discount type is required.");
        var value = ValidateDiscount(type, request.DiscountValue);

        var expiresAt = ToUtc(request.ExpiresAt ?? throw Invalid("expiresAt", "Expiry date is required."));
        if (expiresAt <= now)
        {
            throw Invalid("expiresAt", "Expiry date must be in the future.");
        }

        if (request.MaxUses < 1)
        {
            throw Invalid("maxUses", "Max uses must be at least 1.");
        }

        if (await _db.Coupons.AnyAsync(c => c.Code == code, ct))
        {
            throw new ConflictException("A coupon with this code already exists.");
        }

        var coupon = new Coupon
        {
            Code = code,
            DiscountType = type,
            DiscountValue = value,
            ExpiresAt = expiresAt,
            MaxUses = request.MaxUses,
            UsedCount = 0,
            IsActive = request.IsActive
        };
        _db.Coupons.Add(coupon);

        try
        {
            await _db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException)
        {
            // أدمنين أنشأوا نفس الكود في نفس اللحظة: الـ Unique Index رفض التاني
            throw new ConflictException("A coupon with this code already exists.");
        }

        return ToDto(coupon, now);
    }

    public async Task<CouponDto> UpdateAsync(int id, UpdateCouponRequest request, CancellationToken ct = default)
    {
        RequireAdmin();

        var coupon = await _db.Coupons.FirstOrDefaultAsync(c => c.Id == id, ct)
            ?? throw new NotFoundException(nameof(Coupon), id);

        var type = request.DiscountType
            ?? throw Invalid("discountType", "Discount type is required.");
        var value = ValidateDiscount(type, request.DiscountValue);
        var expiresAt = ToUtc(request.ExpiresAt ?? throw Invalid("expiresAt", "Expiry date is required."));
        var isActive = request.IsActive
            ?? throw Invalid("isActive", "IsActive is required.");

        if (request.MaxUses < coupon.UsedCount)
        {
            throw Invalid("maxUses", $"Max uses cannot be lower than the number of times the coupon was already used ({coupon.UsedCount}).");
        }

        // مسموح تحط تاريخ في الماضي هنا (ده بيخلّي الكوبون ينتهي فوراً)
        coupon.DiscountType = type;
        coupon.DiscountValue = value;
        coupon.ExpiresAt = expiresAt;
        coupon.MaxUses = request.MaxUses;
        coupon.IsActive = isActive;

        await _db.SaveChangesAsync(ct);
        return ToDto(coupon, Now());
    }

    /// حذف كوبون ما اتستخدمش خالص. لو اتستخدم: 409، والحل تعطيله (IsActive = false).
    public async Task DeleteAsync(int id, CancellationToken ct = default)
    {
        RequireAdmin();

        var coupon = await _db.Coupons.FirstOrDefaultAsync(c => c.Id == id, ct)
            ?? throw new NotFoundException(nameof(Coupon), id);

        const string usedMessage = "This coupon has been used and cannot be deleted. Deactivate it instead (IsActive = false).";

        if (coupon.UsedCount > 0 || await _db.Payments.AnyAsync(p => p.CouponId == id, ct))
        {
            throw new ConflictException(usedMessage);
        }

        _db.Coupons.Remove(coupon);

        try
        {
            await _db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException)
        {
            throw new ConflictException(usedMessage);
        }
    }

    private DateTime Now() => _time.GetUtcNow().UtcDateTime;

    /// بيقرّب لقرشين (زي عمود الداتابيز) وبيتحقق: أكبر من صفر، والنسبة مش فوق 100.
    private static decimal ValidateDiscount(DiscountType type, decimal value)
    {
        var rounded = Math.Round(value, 2, MidpointRounding.AwayFromZero);

        if (rounded <= 0m || rounded > MaxDiscountValue)
        {
            throw Invalid("discountValue", "Discount value must be greater than 0.");
        }

        if (type == DiscountType.Percent && rounded > 100m)
        {
            throw Invalid("discountValue", "A percent discount cannot exceed 100.");
        }

        return rounded;
    }

    private static DateTime ToUtc(DateTime value) => value.Kind switch
    {
        DateTimeKind.Local => value.ToUniversalTime(),
        DateTimeKind.Utc => value,
        _ => DateTime.SpecifyKind(value, DateTimeKind.Utc)
    };

    private static CouponDto ToDto(Coupon c, DateTime now) => new(
        c.Id,
        c.Code,
        c.DiscountType,
        c.DiscountValue,
        DateTime.SpecifyKind(c.ExpiresAt, DateTimeKind.Utc),
        c.MaxUses,
        c.UsedCount,
        Math.Max(0, c.MaxUses - c.UsedCount),
        c.IsActive,
        c.ExpiresAt <= now);

    private static AppValidationException Invalid(string field, string message) =>
        new(new Dictionary<string, string[]> { [field] = new[] { message } });

    private void RequireAdmin()
    {
        if (!_currentUser.IsAuthenticated)
        {
            throw new UnauthorizedException();
        }

        if (_currentUser.Role != Roles.Admin)
        {
            throw new ForbiddenException("Only admins can manage coupons.");
        }
    }
}

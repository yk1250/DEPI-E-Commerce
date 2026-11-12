using System.ComponentModel.DataAnnotations;
using Clinic.Domain.Enums;

namespace Clinic.Application.Features.Coupons;

/// كوبون كما يظهر للأدمن (UsedCount بيتحسب من النظام ومش بيتعدّل من الـ API).
public sealed record CouponDto(
    int Id,
    string Code,
    DiscountType DiscountType,
    decimal DiscountValue,
    DateTime ExpiresAt,
    int MaxUses,
    int UsedCount,
    int RemainingUses,
    bool IsActive,
    bool IsExpired);

public class CreateCouponRequest
{
    /// 3 إلى 30 حرف: حروف إنجليزي وأرقام و - و _ (بيتحوّل لحروف كبيرة).
    [Required]
    [RegularExpression("^[A-Za-z0-9_-]{3,30}$", ErrorMessage = "Code must be 3-30 characters: letters, digits, '-' or '_'.")]
    public string Code { get; init; } = string.Empty;

    /// Percent (نسبة، من 0 لـ 100) أو Fixed (مبلغ ثابت).
    [Required]
    [EnumDataType(typeof(DiscountType))]
    public DiscountType? DiscountType { get; init; }

    [Range(0.01, 99999999.99)]
    public decimal DiscountValue { get; init; }

    /// تاريخ الانتهاء (UTC). لازم يكون في المستقبل.
    [Required]
    public DateTime? ExpiresAt { get; init; }

    [Range(1, 1000000)]
    public int MaxUses { get; init; }

    public bool IsActive { get; init; } = true;
}

/// PUT بيستبدل كل الحقول القابلة للتعديل. الكود وUsedCount مش بيتغيروا.
public class UpdateCouponRequest
{
    [Required]
    [EnumDataType(typeof(DiscountType))]
    public DiscountType? DiscountType { get; init; }

    [Range(0.01, 99999999.99)]
    public decimal DiscountValue { get; init; }

    [Required]
    public DateTime? ExpiresAt { get; init; }

    /// لازم يكون أكبر من أو يساوي عدد مرات الاستخدام الحالية.
    [Range(1, 1000000)]
    public int MaxUses { get; init; }

    [Required]
    public bool? IsActive { get; init; }
}

using System.ComponentModel.DataAnnotations;
using Clinic.Domain.Enums;

namespace Clinic.Application.Features.Coupons;

public class ValidateCouponRequest
{
    /// كود الكوبون (مش حساس لحالة الحروف).
    [Required]
    [StringLength(30)]
    public string Code { get; init; } = string.Empty;

    /// الموعد اللي هيتطبّق عليه الكوبون (لازم يكون بتاعك).
    [Range(1, int.MaxValue)]
    public int AppointmentId { get; init; }
}

/// معاينة الخصم. التحقق مش بيستهلك الكوبون: الاستهلاك بيحصل وقت الدفع.
public sealed record CouponValidationDto(
    string Code,
    DiscountType DiscountType,
    decimal DiscountValue,
    decimal OriginalAmount,
    decimal DiscountAmount,
    decimal FinalAmount);

using System.ComponentModel.DataAnnotations;
using Clinic.Domain.Enums;

namespace Clinic.Application.Features.Payments;

public class ConfirmClinicPaymentRequest
{
    /// كود كوبون اختياري. بيتحسب الخصم ويتستهلك الكوبون في نفس لحظة تأكيد الدفع.
    [StringLength(30)]
    public string? CouponCode { get; init; }
}

public sealed record PaymentDto(
    int Id,
    int AppointmentId,
    PaymentMethod Method,
    PaymentStatus Status,
    decimal OriginalAmount,
    decimal DiscountAmount,
    decimal Amount,
    string? CouponCode,
    DateTime? PaidAt);

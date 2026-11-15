using System.ComponentModel.DataAnnotations;

namespace Clinic.Application.Features.Payments;

public class InitiateOnlinePaymentRequest
{
    /// الموعد اللي هتدفع عنه (لازم يكون بتاعك وحالته مستني الدفع).
    [Range(1, int.MaxValue)]
    public int AppointmentId { get; init; }

    /// كود كوبون اختياري. بيتحسب الخصم هنا، وبيتستهلك بعد نجاح الدفع.
    [StringLength(30)]
    public string? CouponCode { get; init; }
}

/// بيانات الدفع الأونلاين: افتح IframeUrl في المتصفح (أو WebView) قبل PayBeforeUtc.
public sealed record OnlinePaymentDto(
    int PaymentId,
    int AppointmentId,
    decimal OriginalAmount,
    decimal DiscountAmount,
    decimal Amount,
    string? CouponCode,
    string IframeUrl,
    DateTime PayBeforeUtc);

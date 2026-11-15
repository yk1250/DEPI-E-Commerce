namespace Clinic.Application.Common.Interfaces;

/// بيانات الفوترة اللي Paymob بيطلبها (الباقي بيتملي "NA").
public sealed record PaymobBilling(string FirstName, string LastName, string Email, string Phone);

/// طلب إنشاء صفحة دفع. المبلغ بالقروش، والـ expiration بالثواني.
public sealed record PaymobCheckoutRequest(
    long AmountCents,
    string MerchantOrderId,
    int ExpirationSeconds,
    PaymobBilling Billing);

/// نتيجة إنشاء صفحة الدفع: رقم الأوردر عند Paymob، وتوكن الدفع، ورابط الـ iframe.
public sealed record PaymobCheckout(string OrderId, string PaymentToken, string IframeUrl);

public interface IPaymobClient
{
    /// بيعمل 3 طلبات لـ Paymob بالترتيب: توكن المصادقة، تسجيل الأوردر، مفتاح الدفع.
    Task<PaymobCheckout> CreateCheckoutAsync(PaymobCheckoutRequest request, CancellationToken ct = default);
}

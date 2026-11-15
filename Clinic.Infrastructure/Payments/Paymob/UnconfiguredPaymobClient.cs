using Clinic.Application.Common.Interfaces;

namespace Clinic.Infrastructure.Payments.Paymob;

/// بيتسجّل لو إعدادات Paymob ناقصة، عشان التطبيق يشتغل عند أي حد من الفريق من غير مفاتيح،
/// والدفع الأونلاين بس هو اللي يرجّع خطأ واضح.
public sealed class UnconfiguredPaymobClient : IPaymobClient
{
    public Task<PaymobCheckout> CreateCheckoutAsync(PaymobCheckoutRequest request, CancellationToken ct = default) =>
        throw new InvalidOperationException(
            "Paymob is not configured. Set Paymob:ApiKey, Paymob:HmacSecret, Paymob:IntegrationId and Paymob:IframeId in user secrets.");
}

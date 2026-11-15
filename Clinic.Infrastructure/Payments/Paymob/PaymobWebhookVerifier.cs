using System.Text.Json;
using Clinic.Application.Common.Interfaces;
using Clinic.Application.Features.Payments;

namespace Clinic.Infrastructure.Payments.Paymob;

public sealed class PaymobWebhookVerifier : IPaymobWebhookVerifier
{
    private readonly PaymobSettings _settings;

    public PaymobWebhookVerifier(PaymobSettings settings)
    {
        _settings = settings;
    }

    public int IntegrationId => _settings.IntegrationId;

    public string Currency => _settings.Currency;

    public bool IsValid(JsonElement transaction, string? providedHmac) =>
        PaymobHmac.IsValid(transaction, _settings.HmacSecret, providedHmac);
}

/// لو إعدادات Paymob ناقصة: كل الإشعارات بتترفض (التحقق بيفشل مقفول، مش مفتوح).
public sealed class UnconfiguredPaymobWebhookVerifier : IPaymobWebhookVerifier
{
    public int IntegrationId => 0;

    public string Currency => "EGP";

    public bool IsValid(JsonElement transaction, string? providedHmac) => false;
}

using System.Text.Json;

namespace Clinic.Application.Common.Interfaces;

/// التحقق من توقيع إشعار Paymob، وإعدادات التكامل المتوقعة (عشان نرفض إشعار من تكامل أو عملة تانية).
public interface IPaymobWebhookVerifier
{
    int IntegrationId { get; }
    string Currency { get; }

    /// transaction هو الـ obj اللي جوه الإشعار. providedHmac هو قيمة ?hmac= من الرابط.
    bool IsValid(JsonElement transaction, string? providedHmac);
}

namespace Clinic.Infrastructure.Payments.Paymob;

/// إعدادات Paymob من User Secrets: Paymob:ApiKey وPaymob:HmacSecret وPaymob:IntegrationId وPaymob:IframeId
/// (واختياري: Paymob:BaseUrl وPaymob:Currency).
public sealed record PaymobSettings(
    string ApiKey,
    string HmacSecret,
    int IntegrationId,
    int IframeId,
    string BaseUrl,
    string Currency)
{
    // الـ record بيطبع كل خصائصه في ToString، فنخفي الأسرار عشان ما تتسرّبش في أي لوج بالغلط
    public override string ToString() => "PaymobSettings { secrets redacted }";
}

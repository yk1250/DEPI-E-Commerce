using System.Text.Json;
using Clinic.Application.Common.Exceptions;
using Clinic.Application.Common.Interfaces;
using Clinic.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Clinic.Application.Features.Payments;

public enum PaymobWebhookResult
{
    /// التوقيع غلط (401، ومفيش أي تغيير في الداتابيز).
    InvalidSignature,

    /// إشعار سليم بس مفيش حاجة تتعمل (مكرر، أو معلّق، أو من تكامل تاني...). الرد 200.
    Ignored,

    /// الإشعار اتطبّق (الدفع اتحوّل لـ Paid أو Failed).
    Processed
}

/// استقبال إشعار Paymob (Transaction Processed).
/// الربط بالدفع بيتم بـ order.id بس (الموقّع)، مش بـ merchant_order_id لأنه مش ضمن الحقول الموقّعة.
/// الأخطاء المؤقتة (زي تعارض الـ RowVersion) بتترمي، فالرد 500 وPaymob بيبعت الإشعار تاني.
public class PaymobWebhookService
{
    private readonly IAppDbContext _db;
    private readonly IPaymobWebhookVerifier _verifier;
    private readonly IPaymentSettler _settler;
    private readonly ICouponRedeemer _redeemer;
    private readonly IAppointmentConfirmer _confirmer;
    private readonly TimeProvider _time;
    private readonly ILogger<PaymobWebhookService> _logger;

    public PaymobWebhookService(
        IAppDbContext db,
        IPaymobWebhookVerifier verifier,
        IPaymentSettler settler,
        ICouponRedeemer redeemer,
        IAppointmentConfirmer confirmer,
        TimeProvider time,
        ILogger<PaymobWebhookService> logger)
    {
        _db = db;
        _verifier = verifier;
        _settler = settler;
        _redeemer = redeemer;
        _confirmer = confirmer;
        _time = time;
        _logger = logger;
    }

    public async Task<PaymobWebhookResult> ProcessAsync(JsonElement body, string? hmac, CancellationToken ct = default)
    {
        if (body.ValueKind != JsonValueKind.Object
            || !body.TryGetProperty("type", out var typeElement)
            || typeElement.ValueKind != JsonValueKind.String
            || typeElement.GetString() != "TRANSACTION")
        {
            return PaymobWebhookResult.Ignored;
        }

        if (!body.TryGetProperty("obj", out var obj) || obj.ValueKind != JsonValueKind.Object)
        {
            _logger.LogWarning("Paymob webhook rejected: the transaction object is missing.");
            return PaymobWebhookResult.InvalidSignature;
        }

        // التوقيع أول حاجة: أي حاجة تانية ما بتتقراش قبله
        if (!_verifier.IsValid(obj, hmac))
        {
            _logger.LogWarning("Paymob webhook rejected: invalid signature.");
            return PaymobWebhookResult.InvalidSignature;
        }

        var tx = Transaction.Read(obj);
        if (tx.TransactionId is null || tx.OrderId is null)
        {
            _logger.LogWarning("Paymob webhook ignored: the transaction id or order id is missing.");
            return PaymobWebhookResult.Ignored;
        }

        var payment = await _db.Payments
            .AsNoTracking()
            .Where(p => p.PaymobOrderId == tx.OrderId && p.Method == PaymentMethod.Online)
            .Select(p => new { p.Id, p.AppointmentId, p.Status, p.Amount, p.CouponId })
            .FirstOrDefaultAsync(ct);

        if (payment is null)
        {
            _logger.LogWarning(
                "Paymob webhook ignored: no payment matches Paymob order {OrderId} (transaction {TransactionId}).",
                tx.OrderId, tx.TransactionId);
            return PaymobWebhookResult.Ignored;
        }

        if (tx.IntegrationId != _verifier.IntegrationId
            || !string.Equals(tx.Currency, _verifier.Currency, StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogError(
                "Paymob webhook ignored: integration or currency mismatch for payment {PaymentId} (transaction {TransactionId}).",
                payment.Id, tx.TransactionId);
            return PaymobWebhookResult.Ignored;
        }

        var expectedCents = (long)Math.Round(payment.Amount * 100m, MidpointRounding.AwayFromZero);
        if (tx.AmountCents != expectedCents)
        {
            _logger.LogError(
                "Paymob webhook ignored: amount mismatch for payment {PaymentId} (expected {Expected}, received {Received}, transaction {TransactionId}).",
                payment.Id, expectedCents, tx.AmountCents, tx.TransactionId);
            return PaymobWebhookResult.Ignored;
        }

        if (tx.IsRefunded || tx.IsVoided)
        {
            _logger.LogInformation(
                "Paymob webhook ignored: transaction {TransactionId} of payment {PaymentId} was refunded or voided (not handled here).",
                tx.TransactionId, payment.Id);
            return PaymobWebhookResult.Ignored;
        }

        if (tx.Pending)
        {
            return PaymobWebhookResult.Ignored;
        }

        var now = _time.GetUtcNow().UtcDateTime;

        if (!tx.Success || tx.ErrorOccurred)
        {
            var failed = await _settler.TryMarkFailedAsync(payment.Id, tx.TransactionId, ct);
            _logger.LogInformation(
                "Paymob transaction {TransactionId} failed for payment {PaymentId} (marked: {Marked}).",
                tx.TransactionId, payment.Id, failed);
            return failed ? PaymobWebhookResult.Processed : PaymobWebhookResult.Ignored;
        }

        if (payment.Status == PaymentStatus.Paid)
        {
            return PaymobWebhookResult.Ignored; // إشعار مكرر
        }

        // الحجز الذري للدفع + استهلاك الكوبون + تأكيد الموعد: يا الكل يا ولا حاجة
        await using var transaction = await _db.Database.BeginTransactionAsync(ct);

        var claimed = await _settler.TryMarkPaidAsync(payment.Id, tx.TransactionId, now, ct);
        if (!claimed)
        {
            return PaymobWebhookResult.Ignored; // إشعار تاني سبقه (أو الدفع اتسترجع)
        }

        if (payment.CouponId is int couponId && !await _redeemer.TryRedeemAsync(couponId, now, ct))
        {
            // العميل دفع فعلاً بالخصم: نحترمه، بس نسجّل إن آخر استخدام اتاخد قبله
            _logger.LogWarning(
                "Coupon {CouponId} was no longer available when payment {PaymentId} settled; the discount was honored.",
                couponId, payment.Id);
        }

        try
        {
            await _confirmer.ConfirmAsync(payment.AppointmentId, ct);
        }
        catch (ConflictException ex)
        {
            // الفلوس وصلت بس الموعد اتلغى أو انتهى: الدفع يفضل Paid ومحتاج استرجاع يدوي
            _logger.LogError(
                "Payment {PaymentId} was paid (transaction {TransactionId}) but appointment {AppointmentId} cannot be confirmed: {Reason} A manual refund is needed.",
                payment.Id, tx.TransactionId, payment.AppointmentId, ex.Message);
        }

        await transaction.CommitAsync(ct);
        return PaymobWebhookResult.Processed;
    }

    private sealed record Transaction(
        string? TransactionId,
        string? OrderId,
        long? AmountCents,
        string? Currency,
        int? IntegrationId,
        bool Success,
        bool Pending,
        bool IsVoided,
        bool IsRefunded,
        bool ErrorOccurred)
    {
        public static Transaction Read(JsonElement obj) => new(
            RawNumberOrString(obj, "id"),
            obj.TryGetProperty("order", out var order) && order.ValueKind == JsonValueKind.Object
                ? RawNumberOrString(order, "id")
                : null,
            obj.TryGetProperty("amount_cents", out var cents) && cents.ValueKind == JsonValueKind.Number && cents.TryGetInt64(out var c)
                ? c
                : null,
            obj.TryGetProperty("currency", out var currency) && currency.ValueKind == JsonValueKind.String
                ? currency.GetString()
                : null,
            obj.TryGetProperty("integration_id", out var integration) && integration.ValueKind == JsonValueKind.Number && integration.TryGetInt32(out var i)
                ? i
                : null,
            IsTrue(obj, "success"),
            IsTrue(obj, "pending"),
            IsTrue(obj, "is_voided"),
            IsTrue(obj, "is_refunded"),
            IsTrue(obj, "error_occured"));

        private static bool IsTrue(JsonElement obj, string name) =>
            obj.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.True;

        private static string? RawNumberOrString(JsonElement obj, string name)
        {
            if (!obj.TryGetProperty(name, out var value))
            {
                return null;
            }

            return value.ValueKind switch
            {
                JsonValueKind.Number => value.GetRawText(),
                JsonValueKind.String => value.GetString(),
                _ => null
            };
        }
    }
}

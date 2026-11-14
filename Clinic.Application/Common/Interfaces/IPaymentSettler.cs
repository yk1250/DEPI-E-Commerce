namespace Clinic.Application.Common.Interfaces;

/// تغيير حالة الدفع بشكل ذري (أمر UPDATE واحد بشرط)، عشان إشعارين متكررين ما ينجحوش الاتنين.
public interface IPaymentSettler
{
    /// Pending أو Failed إلى Paid (وبيسجّل رقم العملية ووقت الدفع). false لو الدفع Paid أو Refunded بالفعل.
    Task<bool> TryMarkPaidAsync(int paymentId, string transactionId, DateTime nowUtc, CancellationToken ct = default);

    /// Pending إلى Failed (وبيسجّل رقم العملية الفاشلة). false لو الدفع مش Pending.
    Task<bool> TryMarkFailedAsync(int paymentId, string transactionId, CancellationToken ct = default);
}

using Clinic.Application.Common.Interfaces;
using Clinic.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace Clinic.Infrastructure.Payments;

/// تغيير حالة الدفع بأمر UPDATE واحد بشرط على الحالة الحالية، فالتنافس بين إشعارين آمن:
///     UPDATE Payments SET Status = Paid, TransactionId = @t, PaidAt = @now
///     WHERE Id = @id AND Status IN (Pending, Failed)
public sealed class PaymentSettler : IPaymentSettler
{
    private readonly IAppDbContext _db;

    public PaymentSettler(IAppDbContext db)
    {
        _db = db;
    }

    public async Task<bool> TryMarkPaidAsync(int paymentId, string transactionId, DateTime nowUtc, CancellationToken ct = default)
    {
        var rows = await _db.Payments
            .Where(p => p.Id == paymentId
                        && (p.Status == PaymentStatus.Pending || p.Status == PaymentStatus.Failed))
            .ExecuteUpdateAsync(s => s
                .SetProperty(p => p.Status, PaymentStatus.Paid)
                .SetProperty(p => p.TransactionId, transactionId)
                .SetProperty(p => p.PaidAt, nowUtc), ct);

        return rows == 1;
    }

    public async Task<bool> TryMarkFailedAsync(int paymentId, string transactionId, CancellationToken ct = default)
    {
        var rows = await _db.Payments
            .Where(p => p.Id == paymentId && p.Status == PaymentStatus.Pending)
            .ExecuteUpdateAsync(s => s
                .SetProperty(p => p.Status, PaymentStatus.Failed)
                .SetProperty(p => p.TransactionId, transactionId), ct);

        return rows == 1;
    }
}

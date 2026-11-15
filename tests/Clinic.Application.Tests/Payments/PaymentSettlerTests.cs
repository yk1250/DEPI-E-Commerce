using Clinic.Application.Tests.Medical;
using Clinic.Domain.Entities;
using Clinic.Domain.Enums;
using Clinic.Infrastructure.Payments;
using Microsoft.EntityFrameworkCore;

namespace Clinic.Application.Tests.Payments;

public class PaymentSettlerTests
{
    private static readonly DateTime Now = new(2026, 11, 7, 10, 0, 0, DateTimeKind.Utc);

    private static async Task<int> AddPaymentAsync(MedicalTestEnv env, PaymentStatus status)
    {
        var appointmentId = await env.AddAppointmentAsync(env.Doctor1, env.PatientA, AppointmentStatus.PendingPayment, Now.AddMinutes(15));
        var payment = new Payment
        {
            AppointmentId = appointmentId,
            Method = PaymentMethod.Online,
            Status = status,
            OriginalAmount = 300m,
            DiscountAmount = 0m,
            Amount = 300m,
            PaymobOrderId = "7001",
            CreatedAt = Now
        };
        env.Db.Payments.Add(payment);
        await env.Db.SaveChangesAsync();
        return payment.Id;
    }

    private static Task<Payment> RowAsync(MedicalTestEnv env, int id) =>
        env.Db.Payments.AsNoTracking().SingleAsync(p => p.Id == id);

    [Fact]
    public async Task Pending_payment_is_marked_paid_with_transaction_and_time()
    {
        using var env = await MedicalTestEnv.CreateAsync();
        var id = await AddPaymentAsync(env, PaymentStatus.Pending);

        var claimed = await new PaymentSettler(env.Db).TryMarkPaidAsync(id, "TX-1", Now);

        Assert.True(claimed);
        var row = await RowAsync(env, id);
        Assert.Equal(PaymentStatus.Paid, row.Status);
        Assert.Equal("TX-1", row.TransactionId);
        Assert.Equal(Now, row.PaidAt);
    }

    [Fact]
    public async Task Only_the_first_of_two_claims_wins()
    {
        using var env = await MedicalTestEnv.CreateAsync();
        var id = await AddPaymentAsync(env, PaymentStatus.Pending);
        var sut = new PaymentSettler(env.Db);

        var first = await sut.TryMarkPaidAsync(id, "TX-1", Now);
        var second = await sut.TryMarkPaidAsync(id, "TX-2", Now.AddMinutes(1));

        Assert.True(first);
        Assert.False(second);
        var row = await RowAsync(env, id);
        Assert.Equal("TX-1", row.TransactionId);
        Assert.Equal(Now, row.PaidAt);
    }

    [Fact]
    public async Task Failed_payment_can_still_be_paid()
    {
        using var env = await MedicalTestEnv.CreateAsync();
        var id = await AddPaymentAsync(env, PaymentStatus.Failed);

        Assert.True(await new PaymentSettler(env.Db).TryMarkPaidAsync(id, "TX-9", Now));
        Assert.Equal(PaymentStatus.Paid, (await RowAsync(env, id)).Status);
    }

    [Fact]
    public async Task Refunded_payment_cannot_be_paid()
    {
        using var env = await MedicalTestEnv.CreateAsync();
        var id = await AddPaymentAsync(env, PaymentStatus.Refunded);

        Assert.False(await new PaymentSettler(env.Db).TryMarkPaidAsync(id, "TX-1", Now));
        Assert.Equal(PaymentStatus.Refunded, (await RowAsync(env, id)).Status);
    }

    [Fact]
    public async Task Unknown_payment_is_not_claimed()
    {
        using var env = await MedicalTestEnv.CreateAsync();

        Assert.False(await new PaymentSettler(env.Db).TryMarkPaidAsync(9999, "TX-1", Now));
    }

    [Fact]
    public async Task Pending_payment_can_be_marked_failed_once()
    {
        using var env = await MedicalTestEnv.CreateAsync();
        var id = await AddPaymentAsync(env, PaymentStatus.Pending);
        var sut = new PaymentSettler(env.Db);

        Assert.True(await sut.TryMarkFailedAsync(id, "TX-F"));
        Assert.False(await sut.TryMarkFailedAsync(id, "TX-F2"));

        var row = await RowAsync(env, id);
        Assert.Equal(PaymentStatus.Failed, row.Status);
        Assert.Equal("TX-F", row.TransactionId);
    }

    [Theory]
    [InlineData(PaymentStatus.Paid)]
    [InlineData(PaymentStatus.Refunded)]
    public async Task Paid_or_refunded_payment_is_never_marked_failed(PaymentStatus status)
    {
        using var env = await MedicalTestEnv.CreateAsync();
        var id = await AddPaymentAsync(env, status);

        Assert.False(await new PaymentSettler(env.Db).TryMarkFailedAsync(id, "TX-F"));
        Assert.Equal(status, (await RowAsync(env, id)).Status);
    }
}

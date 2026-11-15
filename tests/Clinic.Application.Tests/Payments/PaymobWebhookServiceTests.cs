using System.Text.Json;
using Clinic.Application.Common.Interfaces;
using Clinic.Application.Features.Payments;
using Clinic.Application.Tests.Medical;
using Clinic.Domain.Entities;
using Clinic.Domain.Enums;
using Clinic.Infrastructure.Coupons;
using Clinic.Infrastructure.Payments;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace Clinic.Application.Tests.Payments;

public class PaymobWebhookServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 7, 10, 0, 0, TimeSpan.Zero);
    private static DateTime In(TimeSpan span) => Now.UtcDateTime.Add(span);

    private sealed class FakeVerifier : IPaymobWebhookVerifier
    {
        public bool Valid { get; set; } = true;
        public int IntegrationId { get; set; } = 4242;
        public string Currency { get; set; } = "EGP";
        public int Calls { get; private set; }
        public string? LastHmac { get; private set; }

        public bool IsValid(JsonElement transaction, string? providedHmac)
        {
            Calls++;
            LastHmac = providedHmac;
            return Valid;
        }
    }

    private sealed record Setup(int AppointmentId, int PaymentId, int? CouponId);

    private static PaymobWebhookService Service(MedicalTestEnv env, FakeVerifier verifier) =>
        new(
            env.Db,
            verifier,
            new PaymentSettler(env.Db),
            new CouponRedeemer(env.Db),
            new AppointmentConfirmer(env.Db),
            new TestClock(Now),
            NullLogger<PaymobWebhookService>.Instance);

    /// موعد مستني الدفع + دفع أونلاين Pending برقم أوردر Paymob = 7001.
    private static async Task<Setup> ArrangeAsync(
        MedicalTestEnv env,
        bool withCoupon = false,
        AppointmentStatus appointmentStatus = AppointmentStatus.PendingPayment,
        PaymentStatus paymentStatus = PaymentStatus.Pending,
        PaymentMethod method = PaymentMethod.Online,
        int couponUsedCount = 0,
        int couponMaxUses = 100)
    {
        var appointmentId = await env.AddAppointmentAsync(
            env.Doctor1,
            env.PatientA,
            appointmentStatus,
            appointmentStatus == AppointmentStatus.PendingPayment ? In(TimeSpan.FromMinutes(10)) : null);

        int? couponId = null;
        if (withCoupon)
        {
            var coupon = new Coupon
            {
                Code = "WELCOME10",
                DiscountType = DiscountType.Percent,
                DiscountValue = 10m,
                ExpiresAt = In(TimeSpan.FromDays(30)),
                MaxUses = couponMaxUses,
                UsedCount = couponUsedCount,
                IsActive = true
            };
            env.Db.Coupons.Add(coupon);
            await env.Db.SaveChangesAsync();
            couponId = coupon.Id;
        }

        var payment = new Payment
        {
            AppointmentId = appointmentId,
            CouponId = couponId,
            Method = method,
            Status = paymentStatus,
            OriginalAmount = 300m,
            DiscountAmount = withCoupon ? 30m : 0m,
            Amount = withCoupon ? 270m : 300m,
            PaymobOrderId = "7001",
            CreatedAt = In(TimeSpan.FromMinutes(-1))
        };
        env.Db.Payments.Add(payment);
        await env.Db.SaveChangesAsync();

        return new Setup(appointmentId, payment.Id, couponId);
    }

    private static JsonElement Payload(
        long amountCents = 30000,
        bool success = true,
        bool pending = false,
        bool voided = false,
        bool refunded = false,
        bool error = false,
        long transactionId = 555,
        string orderId = "7001",
        int integrationId = 4242,
        string currency = "EGP",
        string? merchantOrderId = null,
        string type = "TRANSACTION")
    {
        var body = new Dictionary<string, object?>
        {
            ["type"] = type,
            ["obj"] = new Dictionary<string, object?>
            {
                ["id"] = transactionId,
                ["pending"] = pending,
                ["amount_cents"] = amountCents,
                ["success"] = success,
                ["is_voided"] = voided,
                ["is_refunded"] = refunded,
                ["error_occured"] = error,
                ["currency"] = currency,
                ["integration_id"] = integrationId,
                ["order"] = new Dictionary<string, object?>
                {
                    ["id"] = long.Parse(orderId),
                    ["merchant_order_id"] = merchantOrderId
                }
            }
        };

        return JsonSerializer.SerializeToElement(body);
    }

    private static Task<Payment> PaymentRow(MedicalTestEnv env, int id) =>
        env.Db.Payments.AsNoTracking().SingleAsync(p => p.Id == id);

    private static Task<Appointment> AppointmentRow(MedicalTestEnv env, int id) =>
        env.Db.Appointments.AsNoTracking().SingleAsync(a => a.Id == id);

    private static Task<int> UsedCount(MedicalTestEnv env, int couponId) =>
        env.Db.Coupons.AsNoTracking().Where(c => c.Id == couponId).Select(c => c.UsedCount).SingleAsync();

    // ---------- التوقيع والشكل ----------

    [Fact]
    public async Task Invalid_signature_is_rejected_and_nothing_changes()
    {
        using var env = await MedicalTestEnv.CreateAsync();
        var s = await ArrangeAsync(env, withCoupon: true);
        var verifier = new FakeVerifier { Valid = false };

        var result = await Service(env, verifier).ProcessAsync(Payload(amountCents: 27000), "bad");

        Assert.Equal(PaymobWebhookResult.InvalidSignature, result);
        Assert.Equal(PaymentStatus.Pending, (await PaymentRow(env, s.PaymentId)).Status);
        Assert.Equal(AppointmentStatus.PendingPayment, (await AppointmentRow(env, s.AppointmentId)).Status);
        Assert.Equal(0, await UsedCount(env, s.CouponId!.Value));
    }

    [Fact]
    public async Task Hmac_from_the_query_is_passed_to_the_verifier()
    {
        using var env = await MedicalTestEnv.CreateAsync();
        await ArrangeAsync(env);
        var verifier = new FakeVerifier();

        await Service(env, verifier).ProcessAsync(Payload(), "abc123");

        Assert.Equal(1, verifier.Calls);
        Assert.Equal("abc123", verifier.LastHmac);
    }

    [Fact]
    public async Task Other_callback_types_are_ignored_without_verifying()
    {
        using var env = await MedicalTestEnv.CreateAsync();
        var s = await ArrangeAsync(env);
        var verifier = new FakeVerifier();

        var result = await Service(env, verifier).ProcessAsync(Payload(type: "TOKEN"), "x");

        Assert.Equal(PaymobWebhookResult.Ignored, result);
        Assert.Equal(0, verifier.Calls);
        Assert.Equal(PaymentStatus.Pending, (await PaymentRow(env, s.PaymentId)).Status);
    }

    [Fact]
    public async Task Body_that_is_not_an_object_is_ignored()
    {
        using var env = await MedicalTestEnv.CreateAsync();
        var verifier = new FakeVerifier();
        var body = JsonDocument.Parse("[1,2]").RootElement.Clone();

        var result = await Service(env, verifier).ProcessAsync(body, "x");

        Assert.Equal(PaymobWebhookResult.Ignored, result);
        Assert.Equal(0, verifier.Calls);
    }

    [Fact]
    public async Task Transaction_without_an_obj_is_rejected()
    {
        using var env = await MedicalTestEnv.CreateAsync();
        var body = JsonDocument.Parse("""{"type":"TRANSACTION"}""").RootElement.Clone();

        var result = await Service(env, new FakeVerifier()).ProcessAsync(body, "x");

        Assert.Equal(PaymobWebhookResult.InvalidSignature, result);
    }

    // ---------- الدفع الناجح ----------

    [Fact]
    public async Task Success_marks_paid_confirms_the_appointment_and_stores_the_transaction()
    {
        using var env = await MedicalTestEnv.CreateAsync();
        var s = await ArrangeAsync(env);

        var result = await Service(env, new FakeVerifier()).ProcessAsync(Payload(transactionId: 987), "ok");

        Assert.Equal(PaymobWebhookResult.Processed, result);
        var payment = await PaymentRow(env, s.PaymentId);
        Assert.Equal(PaymentStatus.Paid, payment.Status);
        Assert.Equal("987", payment.TransactionId);
        Assert.Equal(Now.UtcDateTime, payment.PaidAt);

        var appointment = await AppointmentRow(env, s.AppointmentId);
        Assert.Equal(AppointmentStatus.Confirmed, appointment.Status);
        Assert.Null(appointment.ExpiresAt);
    }

    [Fact]
    public async Task Success_with_a_coupon_consumes_it_once()
    {
        using var env = await MedicalTestEnv.CreateAsync();
        var s = await ArrangeAsync(env, withCoupon: true, couponUsedCount: 4);

        var result = await Service(env, new FakeVerifier()).ProcessAsync(Payload(amountCents: 27000), "ok");

        Assert.Equal(PaymobWebhookResult.Processed, result);
        Assert.Equal(5, await UsedCount(env, s.CouponId!.Value));
    }

    [Fact]
    public async Task Duplicate_success_is_ignored_and_does_not_consume_the_coupon_again()
    {
        using var env = await MedicalTestEnv.CreateAsync();
        var s = await ArrangeAsync(env, withCoupon: true);
        var sut = Service(env, new FakeVerifier());

        var first = await sut.ProcessAsync(Payload(amountCents: 27000), "ok");
        var second = await sut.ProcessAsync(Payload(amountCents: 27000), "ok");

        Assert.Equal(PaymobWebhookResult.Processed, first);
        Assert.Equal(PaymobWebhookResult.Ignored, second);
        Assert.Equal(1, await UsedCount(env, s.CouponId!.Value));
        Assert.Equal(PaymentStatus.Paid, (await PaymentRow(env, s.PaymentId)).Status);
    }

    [Fact]
    public async Task Already_confirmed_appointment_is_left_alone_but_the_payment_is_paid()
    {
        using var env = await MedicalTestEnv.CreateAsync();
        var s = await ArrangeAsync(env, appointmentStatus: AppointmentStatus.Confirmed);

        var result = await Service(env, new FakeVerifier()).ProcessAsync(Payload(), "ok");

        Assert.Equal(PaymobWebhookResult.Processed, result);
        Assert.Equal(PaymentStatus.Paid, (await PaymentRow(env, s.PaymentId)).Status);
        Assert.Equal(AppointmentStatus.Confirmed, (await AppointmentRow(env, s.AppointmentId)).Status);
    }

    [Theory]
    [InlineData(AppointmentStatus.Expired)]
    [InlineData(AppointmentStatus.Cancelled)]
    [InlineData(AppointmentStatus.NoShow)]
    public async Task Payment_for_a_dead_appointment_stays_paid_for_a_manual_refund(AppointmentStatus status)
    {
        using var env = await MedicalTestEnv.CreateAsync();
        var s = await ArrangeAsync(env, appointmentStatus: status);

        var result = await Service(env, new FakeVerifier()).ProcessAsync(Payload(), "ok");

        Assert.Equal(PaymobWebhookResult.Processed, result);
        Assert.Equal(PaymentStatus.Paid, (await PaymentRow(env, s.PaymentId)).Status);
        Assert.Equal(status, (await AppointmentRow(env, s.AppointmentId)).Status);
    }

    [Fact]
    public async Task Exhausted_coupon_at_settlement_still_honors_the_payment()
    {
        using var env = await MedicalTestEnv.CreateAsync();
        var s = await ArrangeAsync(env, withCoupon: true, couponUsedCount: 1, couponMaxUses: 1);

        var result = await Service(env, new FakeVerifier()).ProcessAsync(Payload(amountCents: 27000), "ok");

        Assert.Equal(PaymobWebhookResult.Processed, result);
        Assert.Equal(PaymentStatus.Paid, (await PaymentRow(env, s.PaymentId)).Status);
        Assert.Equal(1, await UsedCount(env, s.CouponId!.Value));
    }

    // ---------- إشعارات مش بتتطبّق ----------

    [Fact]
    public async Task Amount_mismatch_is_ignored()
    {
        using var env = await MedicalTestEnv.CreateAsync();
        var s = await ArrangeAsync(env);

        var result = await Service(env, new FakeVerifier()).ProcessAsync(Payload(amountCents: 100), "ok");

        Assert.Equal(PaymobWebhookResult.Ignored, result);
        Assert.Equal(PaymentStatus.Pending, (await PaymentRow(env, s.PaymentId)).Status);
        Assert.Equal(AppointmentStatus.PendingPayment, (await AppointmentRow(env, s.AppointmentId)).Status);
    }

    [Fact]
    public async Task Currency_mismatch_is_ignored()
    {
        using var env = await MedicalTestEnv.CreateAsync();
        var s = await ArrangeAsync(env);

        var result = await Service(env, new FakeVerifier()).ProcessAsync(Payload(currency: "USD"), "ok");

        Assert.Equal(PaymobWebhookResult.Ignored, result);
        Assert.Equal(PaymentStatus.Pending, (await PaymentRow(env, s.PaymentId)).Status);
    }

    [Fact]
    public async Task Integration_mismatch_is_ignored()
    {
        using var env = await MedicalTestEnv.CreateAsync();
        var s = await ArrangeAsync(env);

        var result = await Service(env, new FakeVerifier()).ProcessAsync(Payload(integrationId: 1111), "ok");

        Assert.Equal(PaymobWebhookResult.Ignored, result);
        Assert.Equal(PaymentStatus.Pending, (await PaymentRow(env, s.PaymentId)).Status);
    }

    [Fact]
    public async Task Unknown_order_is_ignored()
    {
        using var env = await MedicalTestEnv.CreateAsync();
        var s = await ArrangeAsync(env);

        var result = await Service(env, new FakeVerifier()).ProcessAsync(Payload(orderId: "424242"), "ok");

        Assert.Equal(PaymobWebhookResult.Ignored, result);
        Assert.Equal(PaymentStatus.Pending, (await PaymentRow(env, s.PaymentId)).Status);
    }

    [Fact]
    public async Task Unsigned_merchant_order_id_is_never_used_to_find_a_payment()
    {
        using var env = await MedicalTestEnv.CreateAsync();
        var s = await ArrangeAsync(env);

        // order.id مش بتاعنا، لكن merchant_order_id بيشاور على موعدنا: لازم يتجاهل
        var result = await Service(env, new FakeVerifier())
            .ProcessAsync(Payload(orderId: "424242", merchantOrderId: $"A{s.AppointmentId}-evil"), "ok");

        Assert.Equal(PaymobWebhookResult.Ignored, result);
        Assert.Equal(PaymentStatus.Pending, (await PaymentRow(env, s.PaymentId)).Status);
        Assert.Equal(AppointmentStatus.PendingPayment, (await AppointmentRow(env, s.AppointmentId)).Status);
    }

    [Fact]
    public async Task Clinic_payment_is_never_matched_by_a_paymob_order()
    {
        using var env = await MedicalTestEnv.CreateAsync();
        var s = await ArrangeAsync(env, method: PaymentMethod.AtClinic);

        var result = await Service(env, new FakeVerifier()).ProcessAsync(Payload(), "ok");

        Assert.Equal(PaymobWebhookResult.Ignored, result);
        Assert.Equal(PaymentStatus.Pending, (await PaymentRow(env, s.PaymentId)).Status);
    }

    [Fact]
    public async Task Pending_transaction_is_ignored()
    {
        using var env = await MedicalTestEnv.CreateAsync();
        var s = await ArrangeAsync(env);

        var result = await Service(env, new FakeVerifier()).ProcessAsync(Payload(pending: true), "ok");

        Assert.Equal(PaymobWebhookResult.Ignored, result);
        Assert.Equal(PaymentStatus.Pending, (await PaymentRow(env, s.PaymentId)).Status);
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public async Task Refunded_or_voided_transaction_is_ignored(bool refunded, bool voided)
    {
        using var env = await MedicalTestEnv.CreateAsync();
        var s = await ArrangeAsync(env);

        var result = await Service(env, new FakeVerifier()).ProcessAsync(Payload(refunded: refunded, voided: voided), "ok");

        Assert.Equal(PaymobWebhookResult.Ignored, result);
        Assert.Equal(PaymentStatus.Pending, (await PaymentRow(env, s.PaymentId)).Status);
        Assert.Equal(AppointmentStatus.PendingPayment, (await AppointmentRow(env, s.AppointmentId)).Status);
    }

    [Fact]
    public async Task Refunded_payment_cannot_be_paid_again()
    {
        using var env = await MedicalTestEnv.CreateAsync();
        var s = await ArrangeAsync(env, withCoupon: true, paymentStatus: PaymentStatus.Refunded);

        var result = await Service(env, new FakeVerifier()).ProcessAsync(Payload(amountCents: 27000), "ok");

        Assert.Equal(PaymobWebhookResult.Ignored, result);
        Assert.Equal(PaymentStatus.Refunded, (await PaymentRow(env, s.PaymentId)).Status);
        Assert.Equal(0, await UsedCount(env, s.CouponId!.Value));
        Assert.Equal(AppointmentStatus.PendingPayment, (await AppointmentRow(env, s.AppointmentId)).Status);
    }

    // ---------- الفشل ----------

    [Fact]
    public async Task Failed_transaction_marks_the_payment_failed_and_leaves_the_appointment()
    {
        using var env = await MedicalTestEnv.CreateAsync();
        var s = await ArrangeAsync(env, withCoupon: true);

        var result = await Service(env, new FakeVerifier()).ProcessAsync(Payload(amountCents: 27000, success: false, transactionId: 321), "ok");

        Assert.Equal(PaymobWebhookResult.Processed, result);
        var payment = await PaymentRow(env, s.PaymentId);
        Assert.Equal(PaymentStatus.Failed, payment.Status);
        Assert.Equal("321", payment.TransactionId);
        Assert.Equal(AppointmentStatus.PendingPayment, (await AppointmentRow(env, s.AppointmentId)).Status);
        Assert.Equal(0, await UsedCount(env, s.CouponId!.Value));
    }

    [Fact]
    public async Task Error_flag_is_treated_as_a_failure_even_if_success_is_true()
    {
        using var env = await MedicalTestEnv.CreateAsync();
        var s = await ArrangeAsync(env);

        var result = await Service(env, new FakeVerifier()).ProcessAsync(Payload(success: true, error: true), "ok");

        Assert.Equal(PaymobWebhookResult.Processed, result);
        Assert.Equal(PaymentStatus.Failed, (await PaymentRow(env, s.PaymentId)).Status);
    }

    [Fact]
    public async Task Success_after_a_failed_attempt_still_settles()
    {
        using var env = await MedicalTestEnv.CreateAsync();
        var s = await ArrangeAsync(env);
        var sut = Service(env, new FakeVerifier());

        await sut.ProcessAsync(Payload(success: false, transactionId: 1), "ok");
        var result = await sut.ProcessAsync(Payload(success: true, transactionId: 2), "ok");

        Assert.Equal(PaymobWebhookResult.Processed, result);
        var payment = await PaymentRow(env, s.PaymentId);
        Assert.Equal(PaymentStatus.Paid, payment.Status);
        Assert.Equal("2", payment.TransactionId);
        Assert.Equal(AppointmentStatus.Confirmed, (await AppointmentRow(env, s.AppointmentId)).Status);
    }

    [Fact]
    public async Task Failed_notification_after_payment_is_ignored()
    {
        using var env = await MedicalTestEnv.CreateAsync();
        var s = await ArrangeAsync(env);
        var sut = Service(env, new FakeVerifier());

        await sut.ProcessAsync(Payload(success: true, transactionId: 10), "ok");
        var result = await sut.ProcessAsync(Payload(success: false, transactionId: 11), "ok");

        Assert.Equal(PaymobWebhookResult.Ignored, result);
        var payment = await PaymentRow(env, s.PaymentId);
        Assert.Equal(PaymentStatus.Paid, payment.Status);
        Assert.Equal("10", payment.TransactionId);
    }
}

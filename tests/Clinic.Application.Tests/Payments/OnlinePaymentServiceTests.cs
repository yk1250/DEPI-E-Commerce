using Clinic.Application.Common.Exceptions;
using Clinic.Application.Common.Interfaces;
using Clinic.Application.Common.Security;
using Clinic.Application.Features.Coupons;
using Clinic.Application.Features.Payments;
using Clinic.Application.Tests.Medical;
using Clinic.Domain.Entities;
using Clinic.Domain.Enums;
using Clinic.Infrastructure.Coupons;
using Microsoft.EntityFrameworkCore;

namespace Clinic.Application.Tests.Payments;

public class OnlinePaymentServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 7, 10, 0, 0, TimeSpan.Zero);
    private static DateTime In(TimeSpan span) => Now.UtcDateTime.Add(span);

    private sealed class FakePaymobClient : IPaymobClient
    {
        public List<PaymobCheckoutRequest> Requests { get; } = new();
        public Exception? FailWith { get; set; }
        public string OrderId { get; set; } = "7001";

        public Task<PaymobCheckout> CreateCheckoutAsync(PaymobCheckoutRequest request, CancellationToken ct = default)
        {
            Requests.Add(request);
            if (FailWith is not null)
            {
                throw FailWith;
            }

            return Task.FromResult(new PaymobCheckout(
                OrderId,
                $"TOKEN-{OrderId}",
                $"https://pay.test/iframe?payment_token=TOKEN-{OrderId}"));
        }
    }

    private static OnlinePaymentService Service(MedicalTestEnv env, FakePaymobClient paymob, ICurrentUser user)
    {
        var clock = new TestClock(Now);
        var coupons = new CouponService(env.Db, new CouponRedeemer(env.Db), user, clock);
        return new OnlinePaymentService(env.Db, coupons, paymob, user, clock);
    }

    private static TestCurrentUser PatientA(MedicalTestEnv env) =>
        new() { Role = Roles.Patient, PatientId = env.PatientA, UserId = env.PatientAUserId };

    private static Task<int> PendingAppointment(MedicalTestEnv env, int patientId, DateTime? expiresAt = null) =>
        env.AddAppointmentAsync(
            env.Doctor1,
            patientId,
            AppointmentStatus.PendingPayment,
            expiresAt ?? In(TimeSpan.FromMinutes(15)));

    private static async Task<int> AddCouponAsync(
        MedicalTestEnv env,
        string code = "WELCOME10",
        DiscountType type = DiscountType.Percent,
        decimal value = 10m)
    {
        var coupon = new Coupon
        {
            Code = code,
            DiscountType = type,
            DiscountValue = value,
            ExpiresAt = In(TimeSpan.FromDays(30)),
            MaxUses = 100,
            UsedCount = 0,
            IsActive = true
        };
        env.Db.Coupons.Add(coupon);
        await env.Db.SaveChangesAsync();
        return coupon.Id;
    }

    private static async Task AddPaymentAsync(
        MedicalTestEnv env,
        int appointmentId,
        PaymentStatus status,
        PaymentMethod method = PaymentMethod.Online)
    {
        env.Db.Payments.Add(new Payment
        {
            AppointmentId = appointmentId,
            Method = method,
            Status = status,
            OriginalAmount = 300m,
            DiscountAmount = 0m,
            Amount = 300m,
            PaymobOrderId = "OLD",
            TransactionId = status == PaymentStatus.Failed ? "OLD-TX" : null,
            CreatedAt = In(TimeSpan.FromMinutes(-5))
        });
        await env.Db.SaveChangesAsync();
    }

    private static Task<int> PaymentCountAsync(MedicalTestEnv env) => env.Db.Payments.AsNoTracking().CountAsync();

    // ---------- النجاح ----------

    [Fact]
    public async Task Creates_a_pending_online_payment_and_returns_the_checkout_link()
    {
        using var env = await MedicalTestEnv.CreateAsync();
        var paymob = new FakePaymobClient();
        var appointmentId = await PendingAppointment(env, env.PatientA);

        var dto = await Service(env, paymob, PatientA(env)).InitiateAsync(appointmentId, null);

        Assert.Equal(appointmentId, dto.AppointmentId);
        Assert.Equal(300m, dto.OriginalAmount);
        Assert.Equal(0m, dto.DiscountAmount);
        Assert.Equal(300m, dto.Amount);
        Assert.Null(dto.CouponCode);
        Assert.Equal("https://pay.test/iframe?payment_token=TOKEN-7001", dto.IframeUrl);
        Assert.Equal(In(TimeSpan.FromMinutes(15)), dto.PayBeforeUtc);

        var row = await env.Db.Payments.AsNoTracking().SingleAsync();
        Assert.Equal(dto.PaymentId, row.Id);
        Assert.Equal(PaymentMethod.Online, row.Method);
        Assert.Equal(PaymentStatus.Pending, row.Status);
        Assert.Equal("7001", row.PaymobOrderId);
        Assert.Null(row.TransactionId);
        Assert.Null(row.PaidAt);
        Assert.Null(row.CouponId);
    }

    [Fact]
    public async Task Paymob_request_has_cents_expiration_and_a_merchant_order_id_with_the_appointment()
    {
        using var env = await MedicalTestEnv.CreateAsync();
        var paymob = new FakePaymobClient();
        var appointmentId = await PendingAppointment(env, env.PatientA);

        await Service(env, paymob, PatientA(env)).InitiateAsync(appointmentId, null);

        var request = Assert.Single(paymob.Requests);
        Assert.Equal(30000, request.AmountCents);
        Assert.Equal(900, request.ExpirationSeconds);
        Assert.StartsWith($"A{appointmentId}-", request.MerchantOrderId);
        Assert.Equal($"patient{env.PatientA}@clinic.local", request.Billing.Email);
    }

    [Fact]
    public async Task Merchant_order_id_is_different_on_every_attempt()
    {
        using var env = await MedicalTestEnv.CreateAsync();
        var paymob = new FakePaymobClient();
        var appointmentId = await PendingAppointment(env, env.PatientA);
        var sut = Service(env, paymob, PatientA(env));

        await sut.InitiateAsync(appointmentId, null);
        await sut.InitiateAsync(appointmentId, null);

        Assert.Equal(2, paymob.Requests.Count);
        Assert.NotEqual(paymob.Requests[0].MerchantOrderId, paymob.Requests[1].MerchantOrderId);
    }

    [Fact]
    public async Task Expiration_is_capped_at_one_hour()
    {
        using var env = await MedicalTestEnv.CreateAsync();
        var paymob = new FakePaymobClient();
        var appointmentId = await PendingAppointment(env, env.PatientA, In(TimeSpan.FromHours(2)));

        await Service(env, paymob, PatientA(env)).InitiateAsync(appointmentId, null);

        Assert.Equal(3600, paymob.Requests[0].ExpirationSeconds);
    }

    // ---------- الكوبون ----------

    [Fact]
    public async Task Coupon_reduces_the_amount_but_is_not_consumed_yet()
    {
        using var env = await MedicalTestEnv.CreateAsync();
        var paymob = new FakePaymobClient();
        var couponId = await AddCouponAsync(env, "WELCOME10", DiscountType.Percent, 10m);
        var appointmentId = await PendingAppointment(env, env.PatientA);

        var dto = await Service(env, paymob, PatientA(env)).InitiateAsync(appointmentId, "welcome10");

        Assert.Equal(30m, dto.DiscountAmount);
        Assert.Equal(270m, dto.Amount);
        Assert.Equal("WELCOME10", dto.CouponCode);
        Assert.Equal(27000, paymob.Requests[0].AmountCents);

        var row = await env.Db.Payments.AsNoTracking().SingleAsync();
        Assert.Equal(couponId, row.CouponId);
        Assert.Equal(270m, row.Amount);

        var used = await env.Db.Coupons.AsNoTracking().Where(c => c.Id == couponId).Select(c => c.UsedCount).SingleAsync();
        Assert.Equal(0, used);
    }

    [Fact]
    public async Task Invalid_coupon_is_rejected_before_calling_paymob()
    {
        using var env = await MedicalTestEnv.CreateAsync();
        var paymob = new FakePaymobClient();
        var appointmentId = await PendingAppointment(env, env.PatientA);

        await Assert.ThrowsAsync<AppValidationException>(() =>
            Service(env, paymob, PatientA(env)).InitiateAsync(appointmentId, "NOPE"));

        Assert.Empty(paymob.Requests);
        Assert.Equal(0, await PaymentCountAsync(env));
    }

    [Fact]
    public async Task Coupon_covering_the_full_price_is_rejected()
    {
        using var env = await MedicalTestEnv.CreateAsync();
        var paymob = new FakePaymobClient();
        await AddCouponAsync(env, "FREE", DiscountType.Percent, 100m);
        var appointmentId = await PendingAppointment(env, env.PatientA);

        await Assert.ThrowsAsync<AppValidationException>(() =>
            Service(env, paymob, PatientA(env)).InitiateAsync(appointmentId, "FREE"));

        Assert.Empty(paymob.Requests);
        Assert.Equal(0, await PaymentCountAsync(env));
    }

    // ---------- شروط الموعد ----------

    [Theory]
    [InlineData(AppointmentStatus.Confirmed)]
    [InlineData(AppointmentStatus.CheckedIn)]
    [InlineData(AppointmentStatus.Completed)]
    [InlineData(AppointmentStatus.Cancelled)]
    [InlineData(AppointmentStatus.NoShow)]
    [InlineData(AppointmentStatus.Expired)]
    public async Task Only_appointments_awaiting_payment_can_be_paid_online(AppointmentStatus status)
    {
        using var env = await MedicalTestEnv.CreateAsync();
        var paymob = new FakePaymobClient();
        var appointmentId = await env.AddAppointmentAsync(env.Doctor1, env.PatientA, status, In(TimeSpan.FromMinutes(15)));

        await Assert.ThrowsAsync<ConflictException>(() =>
            Service(env, paymob, PatientA(env)).InitiateAsync(appointmentId, null));

        Assert.Empty(paymob.Requests);
    }

    [Fact]
    public async Task Missing_or_expired_payment_window_is_rejected()
    {
        using var env = await MedicalTestEnv.CreateAsync();
        var paymob = new FakePaymobClient();
        var sut = Service(env, paymob, PatientA(env));

        var noExpiry = await env.AddAppointmentAsync(env.Doctor1, env.PatientA, AppointmentStatus.PendingPayment, null);
        await Assert.ThrowsAsync<ConflictException>(() => sut.InitiateAsync(noExpiry, null));

        var past = await env.AddAppointmentAsync(env.Doctor1, env.PatientA, AppointmentStatus.PendingPayment, In(TimeSpan.FromMinutes(-1)));
        await Assert.ThrowsAsync<ConflictException>(() => sut.InitiateAsync(past, null));

        var almost = await env.AddAppointmentAsync(env.Doctor1, env.PatientA, AppointmentStatus.PendingPayment, In(TimeSpan.FromSeconds(30)));
        await Assert.ThrowsAsync<ConflictException>(() => sut.InitiateAsync(almost, null));

        Assert.Empty(paymob.Requests);
    }

    [Fact]
    public async Task Another_patients_appointment_is_not_found()
    {
        using var env = await MedicalTestEnv.CreateAsync();
        var paymob = new FakePaymobClient();
        var appointmentId = await PendingAppointment(env, env.PatientB);

        await Assert.ThrowsAsync<NotFoundException>(() =>
            Service(env, paymob, PatientA(env)).InitiateAsync(appointmentId, null));

        Assert.Empty(paymob.Requests);
    }

    [Fact]
    public async Task Missing_appointment_is_not_found()
    {
        using var env = await MedicalTestEnv.CreateAsync();

        await Assert.ThrowsAsync<NotFoundException>(() =>
            Service(env, new FakePaymobClient(), PatientA(env)).InitiateAsync(9999, null));
    }

    // ---------- الدفع الموجود ----------

    [Theory]
    [InlineData(PaymentStatus.Paid)]
    [InlineData(PaymentStatus.Refunded)]
    public async Task Paid_or_refunded_payment_blocks_a_new_attempt(PaymentStatus status)
    {
        using var env = await MedicalTestEnv.CreateAsync();
        var paymob = new FakePaymobClient();
        var appointmentId = await PendingAppointment(env, env.PatientA);
        await AddPaymentAsync(env, appointmentId, status);

        await Assert.ThrowsAsync<ConflictException>(() =>
            Service(env, paymob, PatientA(env)).InitiateAsync(appointmentId, null));

        Assert.Empty(paymob.Requests);
    }

    [Fact]
    public async Task Clinic_payment_record_blocks_an_online_attempt()
    {
        using var env = await MedicalTestEnv.CreateAsync();
        var paymob = new FakePaymobClient();
        var appointmentId = await PendingAppointment(env, env.PatientA);
        await AddPaymentAsync(env, appointmentId, PaymentStatus.Pending, PaymentMethod.AtClinic);

        await Assert.ThrowsAsync<ConflictException>(() =>
            Service(env, paymob, PatientA(env)).InitiateAsync(appointmentId, null));
    }

    [Fact]
    public async Task Retry_reuses_the_pending_payment_row_with_the_new_paymob_order()
    {
        using var env = await MedicalTestEnv.CreateAsync();
        var paymob = new FakePaymobClient { OrderId = "7002" };
        var appointmentId = await PendingAppointment(env, env.PatientA);
        await AddPaymentAsync(env, appointmentId, PaymentStatus.Pending);

        var dto = await Service(env, paymob, PatientA(env)).InitiateAsync(appointmentId, null);

        Assert.Equal(1, await PaymentCountAsync(env));
        var row = await env.Db.Payments.AsNoTracking().SingleAsync();
        Assert.Equal(dto.PaymentId, row.Id);
        Assert.Equal("7002", row.PaymobOrderId);
        Assert.Equal(PaymentStatus.Pending, row.Status);
    }

    [Fact]
    public async Task Retry_after_a_failed_payment_goes_back_to_pending_and_clears_the_old_transaction()
    {
        using var env = await MedicalTestEnv.CreateAsync();
        var paymob = new FakePaymobClient { OrderId = "7003" };
        var appointmentId = await PendingAppointment(env, env.PatientA);
        await AddPaymentAsync(env, appointmentId, PaymentStatus.Failed);

        await Service(env, paymob, PatientA(env)).InitiateAsync(appointmentId, null);

        var row = await env.Db.Payments.AsNoTracking().SingleAsync();
        Assert.Equal(PaymentStatus.Pending, row.Status);
        Assert.Null(row.TransactionId);
        Assert.Equal("7003", row.PaymobOrderId);
    }

    // ---------- فشل Paymob ----------

    [Fact]
    public async Task Paymob_failure_leaves_no_payment_row()
    {
        using var env = await MedicalTestEnv.CreateAsync();
        var paymob = new FakePaymobClient { FailWith = new InvalidOperationException("paymob down") };
        var appointmentId = await PendingAppointment(env, env.PatientA);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            Service(env, paymob, PatientA(env)).InitiateAsync(appointmentId, null));

        Assert.Equal(0, await PaymentCountAsync(env));
    }

    [Fact]
    public async Task Paymob_failure_leaves_an_existing_pending_row_unchanged()
    {
        using var env = await MedicalTestEnv.CreateAsync();
        var paymob = new FakePaymobClient { FailWith = new InvalidOperationException("paymob down") };
        var appointmentId = await PendingAppointment(env, env.PatientA);
        await AddPaymentAsync(env, appointmentId, PaymentStatus.Pending);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            Service(env, paymob, PatientA(env)).InitiateAsync(appointmentId, null));

        var row = await env.Db.Payments.AsNoTracking().SingleAsync();
        Assert.Equal("OLD", row.PaymobOrderId);
    }

    // ---------- الصلاحيات ----------

    [Theory]
    [InlineData(Roles.Doctor)]
    [InlineData(Roles.Admin)]
    [InlineData(Roles.Receptionist)]
    public async Task Only_patients_can_start_an_online_payment(string role)
    {
        using var env = await MedicalTestEnv.CreateAsync();
        var appointmentId = await PendingAppointment(env, env.PatientA);
        var user = new TestCurrentUser { Role = role, PatientId = env.PatientA, DoctorId = env.Doctor1, ClinicId = env.ClinicId };

        await Assert.ThrowsAsync<ForbiddenException>(() =>
            Service(env, new FakePaymobClient(), user).InitiateAsync(appointmentId, null));
    }

    [Fact]
    public async Task Unauthenticated_start_gets_401()
    {
        using var env = await MedicalTestEnv.CreateAsync();
        var user = new TestCurrentUser { IsAuthenticated = false };

        await Assert.ThrowsAsync<UnauthorizedException>(() =>
            Service(env, new FakePaymobClient(), user).InitiateAsync(1, null));
    }
}

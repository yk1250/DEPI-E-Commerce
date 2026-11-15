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

public class ClinicPaymentServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 7, 10, 0, 0, TimeSpan.Zero);

    private static ClinicPaymentService Service(MedicalTestEnv env, ICurrentUser user)
    {
        var clock = new TestClock(Now);
        var coupons = new CouponService(env.Db, new CouponRedeemer(env.Db), user, clock);
        return new ClinicPaymentService(env.Db, coupons, user, clock);
    }

    private static TestCurrentUser Receptionist(MedicalTestEnv env) =>
        new() { Role = Roles.Receptionist, ClinicId = env.ClinicId };

    private static async Task<int> AddCouponAsync(
        MedicalTestEnv env,
        string code = "WELCOME10",
        DiscountType type = DiscountType.Percent,
        decimal value = 10m,
        int maxUses = 100,
        int usedCount = 0)
    {
        var coupon = new Coupon
        {
            Code = code,
            DiscountType = type,
            DiscountValue = value,
            ExpiresAt = Now.UtcDateTime.AddDays(30),
            MaxUses = maxUses,
            UsedCount = usedCount,
            IsActive = true
        };
        env.Db.Coupons.Add(coupon);
        await env.Db.SaveChangesAsync();
        return coupon.Id;
    }

    private static async Task<int> UsedCountAsync(MedicalTestEnv env, int couponId) =>
        await env.Db.Coupons.AsNoTracking().Where(c => c.Id == couponId).Select(c => c.UsedCount).SingleAsync();

    private static async Task<int> PaymentCountAsync(MedicalTestEnv env) =>
        await env.Db.Payments.AsNoTracking().CountAsync();

    private static async Task AddPaymentAsync(MedicalTestEnv env, int appointmentId, PaymentStatus status)
    {
        env.Db.Payments.Add(new Payment
        {
            AppointmentId = appointmentId,
            Method = PaymentMethod.Online,
            Status = status,
            OriginalAmount = 300m,
            DiscountAmount = 0m,
            Amount = 300m,
            CreatedAt = Now.UtcDateTime
        });
        await env.Db.SaveChangesAsync();
    }

    // ---------- التأكيد الناجح ----------

    [Fact]
    public async Task Receptionist_confirms_a_clinic_payment_without_a_coupon()
    {
        using var env = await MedicalTestEnv.CreateAsync();
        var appointmentId = await env.AddAppointmentAsync(env.Doctor1, env.PatientA, AppointmentStatus.Confirmed);

        var dto = await Service(env, Receptionist(env)).ConfirmAsync(appointmentId, null);

        Assert.Equal(appointmentId, dto.AppointmentId);
        Assert.Equal(PaymentMethod.AtClinic, dto.Method);
        Assert.Equal(PaymentStatus.Paid, dto.Status);
        Assert.Equal(300m, dto.OriginalAmount);
        Assert.Equal(0m, dto.DiscountAmount);
        Assert.Equal(300m, dto.Amount);
        Assert.Null(dto.CouponCode);
        Assert.Equal(Now.UtcDateTime, dto.PaidAt);

        var row = await env.Db.Payments.AsNoTracking().SingleAsync();
        Assert.Equal(appointmentId, row.AppointmentId);
        Assert.Equal(PaymentMethod.AtClinic, row.Method);
        Assert.Equal(PaymentStatus.Paid, row.Status);
        Assert.Null(row.CouponId);
        Assert.Equal(Now.UtcDateTime, row.PaidAt);
    }

    [Theory]
    [InlineData(AppointmentStatus.Confirmed)]
    [InlineData(AppointmentStatus.CheckedIn)]
    public async Task Confirm_is_allowed_for_confirmed_and_checked_in_appointments(AppointmentStatus status)
    {
        using var env = await MedicalTestEnv.CreateAsync();
        var appointmentId = await env.AddAppointmentAsync(env.Doctor1, env.PatientA, status);

        var dto = await Service(env, Receptionist(env)).ConfirmAsync(appointmentId, null);

        Assert.Equal(PaymentStatus.Paid, dto.Status);
    }

    [Theory]
    [InlineData(AppointmentStatus.PendingPayment)]
    [InlineData(AppointmentStatus.Completed)]
    [InlineData(AppointmentStatus.Cancelled)]
    [InlineData(AppointmentStatus.NoShow)]
    [InlineData(AppointmentStatus.Expired)]
    public async Task Confirm_is_rejected_for_other_statuses(AppointmentStatus status)
    {
        using var env = await MedicalTestEnv.CreateAsync();
        var appointmentId = await env.AddAppointmentAsync(env.Doctor1, env.PatientA, status);

        await Assert.ThrowsAsync<ConflictException>(() =>
            Service(env, Receptionist(env)).ConfirmAsync(appointmentId, null));

        Assert.Equal(0, await PaymentCountAsync(env));
    }

    // ---------- الكوبون ----------

    [Fact]
    public async Task Percent_coupon_reduces_the_amount_and_is_consumed_once()
    {
        using var env = await MedicalTestEnv.CreateAsync();
        var couponId = await AddCouponAsync(env, "WELCOME10", DiscountType.Percent, 10m, usedCount: 1);
        var appointmentId = await env.AddAppointmentAsync(env.Doctor1, env.PatientA, AppointmentStatus.Confirmed);

        var dto = await Service(env, Receptionist(env)).ConfirmAsync(appointmentId, "WELCOME10");

        Assert.Equal(300m, dto.OriginalAmount);
        Assert.Equal(30m, dto.DiscountAmount);
        Assert.Equal(270m, dto.Amount);
        Assert.Equal("WELCOME10", dto.CouponCode);

        var row = await env.Db.Payments.AsNoTracking().SingleAsync();
        Assert.Equal(couponId, row.CouponId);
        Assert.Equal(270m, row.Amount);
        Assert.Equal(2, await UsedCountAsync(env, couponId));
    }

    [Fact]
    public async Task Coupon_code_is_matched_ignoring_case_and_spaces()
    {
        using var env = await MedicalTestEnv.CreateAsync();
        await AddCouponAsync(env, "WELCOME10");
        var appointmentId = await env.AddAppointmentAsync(env.Doctor1, env.PatientA, AppointmentStatus.Confirmed);

        var dto = await Service(env, Receptionist(env)).ConfirmAsync(appointmentId, "  welcome10 ");

        Assert.Equal(270m, dto.Amount);
    }

    [Fact]
    public async Task Fixed_coupon_larger_than_the_price_gives_a_zero_amount()
    {
        using var env = await MedicalTestEnv.CreateAsync();
        await AddCouponAsync(env, "BIG", DiscountType.Fixed, 500m);
        var appointmentId = await env.AddAppointmentAsync(env.Doctor1, env.PatientA, AppointmentStatus.Confirmed);

        var dto = await Service(env, Receptionist(env)).ConfirmAsync(appointmentId, "BIG");

        Assert.Equal(300m, dto.DiscountAmount);
        Assert.Equal(0m, dto.Amount);
    }

    [Fact]
    public async Task Blank_coupon_code_means_no_coupon()
    {
        using var env = await MedicalTestEnv.CreateAsync();
        var couponId = await AddCouponAsync(env);
        var appointmentId = await env.AddAppointmentAsync(env.Doctor1, env.PatientA, AppointmentStatus.Confirmed);

        var dto = await Service(env, Receptionist(env)).ConfirmAsync(appointmentId, "   ");

        Assert.Equal(300m, dto.Amount);
        Assert.Equal(0, await UsedCountAsync(env, couponId));
    }

    [Fact]
    public async Task Invalid_coupon_is_rejected_and_nothing_is_saved()
    {
        using var env = await MedicalTestEnv.CreateAsync();
        var couponId = await AddCouponAsync(env, "WELCOME10");
        var appointmentId = await env.AddAppointmentAsync(env.Doctor1, env.PatientA, AppointmentStatus.Confirmed);

        await Assert.ThrowsAsync<AppValidationException>(() =>
            Service(env, Receptionist(env)).ConfirmAsync(appointmentId, "NOPE"));

        Assert.Equal(0, await PaymentCountAsync(env));
        Assert.Equal(0, await UsedCountAsync(env, couponId));
    }

    [Fact]
    public async Task Fully_used_coupon_is_rejected_and_nothing_is_saved()
    {
        using var env = await MedicalTestEnv.CreateAsync();
        var couponId = await AddCouponAsync(env, "WELCOME10", maxUses: 2, usedCount: 2);
        var appointmentId = await env.AddAppointmentAsync(env.Doctor1, env.PatientA, AppointmentStatus.Confirmed);

        await Assert.ThrowsAsync<AppValidationException>(() =>
            Service(env, Receptionist(env)).ConfirmAsync(appointmentId, "WELCOME10"));

        Assert.Equal(0, await PaymentCountAsync(env));
        Assert.Equal(2, await UsedCountAsync(env, couponId));
    }

    [Fact]
    public async Task Last_coupon_use_goes_to_the_first_payment_only()
    {
        using var env = await MedicalTestEnv.CreateAsync();
        var couponId = await AddCouponAsync(env, "ONCE", maxUses: 1);
        var first = await env.AddAppointmentAsync(env.Doctor1, env.PatientA, AppointmentStatus.Confirmed);
        var second = await env.AddAppointmentAsync(env.Doctor1, env.PatientB, AppointmentStatus.Confirmed);
        var sut = Service(env, Receptionist(env));

        await sut.ConfirmAsync(first, "ONCE");
        await Assert.ThrowsAsync<AppValidationException>(() => sut.ConfirmAsync(second, "ONCE"));

        Assert.Equal(1, await PaymentCountAsync(env));
        Assert.Equal(1, await UsedCountAsync(env, couponId));
    }

    // ---------- منع الدفع المزدوج ----------

    [Fact]
    public async Task Second_confirm_of_the_same_appointment_is_rejected_and_does_not_consume_the_coupon_again()
    {
        using var env = await MedicalTestEnv.CreateAsync();
        var couponId = await AddCouponAsync(env);
        var appointmentId = await env.AddAppointmentAsync(env.Doctor1, env.PatientA, AppointmentStatus.Confirmed);
        var sut = Service(env, Receptionist(env));

        await sut.ConfirmAsync(appointmentId, "WELCOME10");
        await Assert.ThrowsAsync<ConflictException>(() => sut.ConfirmAsync(appointmentId, "WELCOME10"));

        Assert.Equal(1, await PaymentCountAsync(env));
        Assert.Equal(1, await UsedCountAsync(env, couponId));
    }

    [Fact]
    public async Task Already_paid_appointment_is_rejected()
    {
        using var env = await MedicalTestEnv.CreateAsync();
        var couponId = await AddCouponAsync(env);
        var appointmentId = await env.AddAppointmentAsync(env.Doctor1, env.PatientA, AppointmentStatus.Confirmed);
        await AddPaymentAsync(env, appointmentId, PaymentStatus.Paid);

        var ex = await Assert.ThrowsAsync<ConflictException>(() =>
            Service(env, Receptionist(env)).ConfirmAsync(appointmentId, "WELCOME10"));

        Assert.Contains("already paid", ex.Message);
        Assert.Equal(0, await UsedCountAsync(env, couponId));
    }

    [Fact]
    public async Task Existing_unpaid_payment_record_is_rejected()
    {
        using var env = await MedicalTestEnv.CreateAsync();
        var appointmentId = await env.AddAppointmentAsync(env.Doctor1, env.PatientA, AppointmentStatus.Confirmed);
        await AddPaymentAsync(env, appointmentId, PaymentStatus.Pending);

        await Assert.ThrowsAsync<ConflictException>(() =>
            Service(env, Receptionist(env)).ConfirmAsync(appointmentId, null));

        var row = await env.Db.Payments.AsNoTracking().SingleAsync();
        Assert.Equal(PaymentStatus.Pending, row.Status);
    }

    // ---------- العيادة والصلاحيات ----------

    [Fact]
    public async Task Receptionist_of_another_clinic_is_forbidden()
    {
        using var env = await MedicalTestEnv.CreateAsync();
        var appointmentId = await env.AddAppointmentAsync(env.Doctor1, env.PatientA, AppointmentStatus.Confirmed);
        var otherClinic = new TestCurrentUser { Role = Roles.Receptionist, ClinicId = env.ClinicId + 1 };

        await Assert.ThrowsAsync<ForbiddenException>(() =>
            Service(env, otherClinic).ConfirmAsync(appointmentId, null));

        Assert.Equal(0, await PaymentCountAsync(env));
    }

    [Fact]
    public async Task Missing_appointment_is_not_found()
    {
        using var env = await MedicalTestEnv.CreateAsync();

        await Assert.ThrowsAsync<NotFoundException>(() =>
            Service(env, Receptionist(env)).ConfirmAsync(9999, null));
    }

    [Fact]
    public async Task Receptionist_without_a_clinic_claim_is_forbidden()
    {
        using var env = await MedicalTestEnv.CreateAsync();
        var appointmentId = await env.AddAppointmentAsync(env.Doctor1, env.PatientA, AppointmentStatus.Confirmed);
        var user = new TestCurrentUser { Role = Roles.Receptionist, ClinicId = null };

        await Assert.ThrowsAsync<ForbiddenException>(() =>
            Service(env, user).ConfirmAsync(appointmentId, null));
    }

    [Theory]
    [InlineData(Roles.Patient)]
    [InlineData(Roles.Doctor)]
    [InlineData(Roles.Admin)]
    public async Task Only_receptionists_can_confirm_clinic_payments(string role)
    {
        using var env = await MedicalTestEnv.CreateAsync();
        var appointmentId = await env.AddAppointmentAsync(env.Doctor1, env.PatientA, AppointmentStatus.Confirmed);
        var user = new TestCurrentUser
        {
            Role = role,
            PatientId = env.PatientA,
            DoctorId = env.Doctor1,
            ClinicId = env.ClinicId
        };

        await Assert.ThrowsAsync<ForbiddenException>(() =>
            Service(env, user).ConfirmAsync(appointmentId, null));

        Assert.Equal(0, await PaymentCountAsync(env));
    }

    [Fact]
    public async Task Unauthenticated_confirm_gets_401()
    {
        using var env = await MedicalTestEnv.CreateAsync();
        var user = new TestCurrentUser { IsAuthenticated = false };

        await Assert.ThrowsAsync<UnauthorizedException>(() =>
            Service(env, user).ConfirmAsync(1, null));
    }
}

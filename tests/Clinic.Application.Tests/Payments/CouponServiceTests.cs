using Clinic.Application.Common.Exceptions;
using Clinic.Application.Common.Interfaces;
using Clinic.Application.Common.Security;
using Clinic.Application.Features.Coupons;
using Clinic.Application.Tests.Medical;
using Clinic.Domain.Entities;
using Clinic.Domain.Enums;
using Clinic.Infrastructure.Coupons;
using Microsoft.EntityFrameworkCore;

namespace Clinic.Application.Tests.Payments;

public class CouponServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 7, 10, 0, 0, TimeSpan.Zero);

    private static CouponService Service(MedicalTestEnv env, ICurrentUser user) =>
        new(env.Db, new CouponRedeemer(env.Db), user, new TestClock(Now));

    private static TestCurrentUser PatientA(MedicalTestEnv env) =>
        new() { Role = Roles.Patient, PatientId = env.PatientA, UserId = env.PatientAUserId };

    private static async Task<int> AddCouponAsync(
        MedicalTestEnv env,
        string code = "WELCOME10",
        DiscountType type = DiscountType.Percent,
        decimal value = 10m,
        DateTime? expiresAt = null,
        int maxUses = 100,
        int usedCount = 0,
        bool isActive = true)
    {
        var coupon = new Coupon
        {
            Code = code,
            DiscountType = type,
            DiscountValue = value,
            ExpiresAt = expiresAt ?? Now.UtcDateTime.AddDays(30),
            MaxUses = maxUses,
            UsedCount = usedCount,
            IsActive = isActive
        };
        env.Db.Coupons.Add(coupon);
        await env.Db.SaveChangesAsync();
        return coupon.Id;
    }

    private static ValidateCouponRequest Request(int appointmentId, string code = "WELCOME10") =>
        new() { AppointmentId = appointmentId, Code = code };

    private static async Task<int> UsedCountAsync(MedicalTestEnv env, int couponId) =>
        await env.Db.Coupons.AsNoTracking().Where(c => c.Id == couponId).Select(c => c.UsedCount).SingleAsync();

    // ---------- معاينة الخصم ----------

    [Fact]
    public async Task Percent_coupon_returns_the_discount_preview()
    {
        using var env = await MedicalTestEnv.CreateAsync();
        await AddCouponAsync(env, "WELCOME10", DiscountType.Percent, 10m);
        var appointmentId = await env.AddAppointmentAsync(env.Doctor1, env.PatientA, AppointmentStatus.PendingPayment);

        var result = await Service(env, PatientA(env)).ValidateAsync(Request(appointmentId));

        Assert.Equal("WELCOME10", result.Code);
        Assert.Equal(DiscountType.Percent, result.DiscountType);
        Assert.Equal(10m, result.DiscountValue);
        Assert.Equal(300m, result.OriginalAmount);
        Assert.Equal(30m, result.DiscountAmount);
        Assert.Equal(270m, result.FinalAmount);
    }

    [Fact]
    public async Task Fixed_coupon_returns_the_discount_preview()
    {
        using var env = await MedicalTestEnv.CreateAsync();
        await AddCouponAsync(env, "FIXED50", DiscountType.Fixed, 50m);
        var appointmentId = await env.AddAppointmentAsync(env.Doctor1, env.PatientA, AppointmentStatus.Confirmed);

        var result = await Service(env, PatientA(env)).ValidateAsync(Request(appointmentId, "FIXED50"));

        Assert.Equal(50m, result.DiscountAmount);
        Assert.Equal(250m, result.FinalAmount);
    }

    [Fact]
    public async Task Fixed_coupon_larger_than_the_price_gives_a_zero_final_amount()
    {
        using var env = await MedicalTestEnv.CreateAsync();
        await AddCouponAsync(env, "BIG", DiscountType.Fixed, 500m);
        var appointmentId = await env.AddAppointmentAsync(env.Doctor1, env.PatientA, AppointmentStatus.PendingPayment);

        var result = await Service(env, PatientA(env)).ValidateAsync(Request(appointmentId, "BIG"));

        Assert.Equal(300m, result.DiscountAmount);
        Assert.Equal(0m, result.FinalAmount);
    }

    [Fact]
    public async Task Code_is_matched_ignoring_case_and_surrounding_spaces()
    {
        using var env = await MedicalTestEnv.CreateAsync();
        await AddCouponAsync(env, "WELCOME10");
        var appointmentId = await env.AddAppointmentAsync(env.Doctor1, env.PatientA, AppointmentStatus.PendingPayment);

        var result = await Service(env, PatientA(env)).ValidateAsync(Request(appointmentId, "  welcome10 "));

        Assert.Equal("WELCOME10", result.Code);
    }

    [Fact]
    public async Task Validate_does_not_consume_the_coupon()
    {
        using var env = await MedicalTestEnv.CreateAsync();
        var couponId = await AddCouponAsync(env, usedCount: 3);
        var appointmentId = await env.AddAppointmentAsync(env.Doctor1, env.PatientA, AppointmentStatus.PendingPayment);

        await Service(env, PatientA(env)).ValidateAsync(Request(appointmentId));

        Assert.Equal(3, await UsedCountAsync(env, couponId));
    }

    // ---------- أكواد مش صالحة (كلها نفس الرد 400) ----------

    [Fact]
    public async Task Unknown_code_is_rejected()
    {
        using var env = await MedicalTestEnv.CreateAsync();
        var appointmentId = await env.AddAppointmentAsync(env.Doctor1, env.PatientA, AppointmentStatus.PendingPayment);

        await Assert.ThrowsAsync<AppValidationException>(() =>
            Service(env, PatientA(env)).ValidateAsync(Request(appointmentId, "NOPE")));
    }

    [Fact]
    public async Task Inactive_coupon_is_rejected()
    {
        using var env = await MedicalTestEnv.CreateAsync();
        await AddCouponAsync(env, isActive: false);
        var appointmentId = await env.AddAppointmentAsync(env.Doctor1, env.PatientA, AppointmentStatus.PendingPayment);

        await Assert.ThrowsAsync<AppValidationException>(() =>
            Service(env, PatientA(env)).ValidateAsync(Request(appointmentId)));
    }

    [Fact]
    public async Task Expired_coupon_is_rejected()
    {
        using var env = await MedicalTestEnv.CreateAsync();
        await AddCouponAsync(env, expiresAt: Now.UtcDateTime.AddMinutes(-1));
        var appointmentId = await env.AddAppointmentAsync(env.Doctor1, env.PatientA, AppointmentStatus.PendingPayment);

        await Assert.ThrowsAsync<AppValidationException>(() =>
            Service(env, PatientA(env)).ValidateAsync(Request(appointmentId)));
    }

    [Fact]
    public async Task Fully_used_coupon_is_rejected()
    {
        using var env = await MedicalTestEnv.CreateAsync();
        await AddCouponAsync(env, maxUses: 5, usedCount: 5);
        var appointmentId = await env.AddAppointmentAsync(env.Doctor1, env.PatientA, AppointmentStatus.PendingPayment);

        await Assert.ThrowsAsync<AppValidationException>(() =>
            Service(env, PatientA(env)).ValidateAsync(Request(appointmentId)));
    }

    [Fact]
    public async Task Coupon_with_one_use_left_is_still_valid()
    {
        using var env = await MedicalTestEnv.CreateAsync();
        await AddCouponAsync(env, maxUses: 5, usedCount: 4);
        var appointmentId = await env.AddAppointmentAsync(env.Doctor1, env.PatientA, AppointmentStatus.PendingPayment);

        var result = await Service(env, PatientA(env)).ValidateAsync(Request(appointmentId));

        Assert.Equal(30m, result.DiscountAmount);
    }

    [Fact]
    public async Task Blank_code_is_rejected()
    {
        using var env = await MedicalTestEnv.CreateAsync();
        var appointmentId = await env.AddAppointmentAsync(env.Doctor1, env.PatientA, AppointmentStatus.PendingPayment);

        await Assert.ThrowsAsync<AppValidationException>(() =>
            Service(env, PatientA(env)).ValidateAsync(Request(appointmentId, "   ")));
    }

    // ---------- الموعد ----------

    [Fact]
    public async Task Another_patients_appointment_is_not_found()
    {
        using var env = await MedicalTestEnv.CreateAsync();
        await AddCouponAsync(env);
        var appointmentId = await env.AddAppointmentAsync(env.Doctor1, env.PatientB, AppointmentStatus.PendingPayment);

        await Assert.ThrowsAsync<NotFoundException>(() =>
            Service(env, PatientA(env)).ValidateAsync(Request(appointmentId)));
    }

    [Fact]
    public async Task Missing_appointment_is_not_found()
    {
        using var env = await MedicalTestEnv.CreateAsync();
        await AddCouponAsync(env);

        await Assert.ThrowsAsync<NotFoundException>(() =>
            Service(env, PatientA(env)).ValidateAsync(Request(9999)));
    }

    [Theory]
    [InlineData(AppointmentStatus.Completed)]
    [InlineData(AppointmentStatus.Cancelled)]
    [InlineData(AppointmentStatus.NoShow)]
    [InlineData(AppointmentStatus.Expired)]
    public async Task Appointment_not_awaiting_payment_is_rejected(AppointmentStatus status)
    {
        using var env = await MedicalTestEnv.CreateAsync();
        await AddCouponAsync(env);
        var appointmentId = await env.AddAppointmentAsync(env.Doctor1, env.PatientA, status);

        await Assert.ThrowsAsync<ConflictException>(() =>
            Service(env, PatientA(env)).ValidateAsync(Request(appointmentId)));
    }

    [Fact]
    public async Task Already_paid_appointment_is_rejected()
    {
        using var env = await MedicalTestEnv.CreateAsync();
        await AddCouponAsync(env);
        var appointmentId = await env.AddAppointmentAsync(env.Doctor1, env.PatientA, AppointmentStatus.Confirmed);
        env.Db.Payments.Add(new Payment
        {
            AppointmentId = appointmentId,
            Method = PaymentMethod.Online,
            Status = PaymentStatus.Paid,
            OriginalAmount = 300m,
            DiscountAmount = 0m,
            Amount = 300m,
            CreatedAt = Now.UtcDateTime
        });
        await env.Db.SaveChangesAsync();

        await Assert.ThrowsAsync<ConflictException>(() =>
            Service(env, PatientA(env)).ValidateAsync(Request(appointmentId)));
    }

    // ---------- الصلاحيات ----------

    [Theory]
    [InlineData(Roles.Doctor)]
    [InlineData(Roles.Admin)]
    [InlineData(Roles.Receptionist)]
    public async Task Only_patients_can_validate_coupons(string role)
    {
        using var env = await MedicalTestEnv.CreateAsync();
        await AddCouponAsync(env);
        var appointmentId = await env.AddAppointmentAsync(env.Doctor1, env.PatientA, AppointmentStatus.PendingPayment);
        var user = new TestCurrentUser { Role = role, DoctorId = env.Doctor1, PatientId = env.PatientA };

        await Assert.ThrowsAsync<ForbiddenException>(() =>
            Service(env, user).ValidateAsync(Request(appointmentId)));
    }

    [Fact]
    public async Task Unauthenticated_validate_gets_401()
    {
        using var env = await MedicalTestEnv.CreateAsync();
        var user = new TestCurrentUser { IsAuthenticated = false };

        await Assert.ThrowsAsync<UnauthorizedException>(() =>
            Service(env, user).ValidateAsync(Request(1)));
    }

    // ---------- الاستهلاك الذري ----------

    [Fact]
    public async Task Redeem_increments_the_used_count_by_one()
    {
        using var env = await MedicalTestEnv.CreateAsync();
        var couponId = await AddCouponAsync(env, usedCount: 2);

        await Service(env, PatientA(env)).RedeemAsync(couponId);

        Assert.Equal(3, await UsedCountAsync(env, couponId));
    }

    [Fact]
    public async Task Last_use_can_be_taken_once_and_only_once()
    {
        using var env = await MedicalTestEnv.CreateAsync();
        var couponId = await AddCouponAsync(env, maxUses: 1, usedCount: 0);
        var sut = Service(env, PatientA(env));

        await sut.RedeemAsync(couponId);
        await Assert.ThrowsAsync<ConflictException>(() => sut.RedeemAsync(couponId));

        Assert.Equal(1, await UsedCountAsync(env, couponId));
    }

    [Fact]
    public async Task Used_count_never_goes_above_max_uses()
    {
        using var env = await MedicalTestEnv.CreateAsync();
        var couponId = await AddCouponAsync(env, maxUses: 3, usedCount: 0);
        var sut = Service(env, PatientA(env));

        var successes = 0;
        for (var i = 0; i < 10; i++)
        {
            try
            {
                await sut.RedeemAsync(couponId);
                successes++;
            }
            catch (ConflictException)
            {
            }
        }

        Assert.Equal(3, successes);
        Assert.Equal(3, await UsedCountAsync(env, couponId));
    }

    [Fact]
    public async Task Expired_coupon_cannot_be_redeemed()
    {
        using var env = await MedicalTestEnv.CreateAsync();
        var couponId = await AddCouponAsync(env, expiresAt: Now.UtcDateTime.AddMinutes(-1));

        await Assert.ThrowsAsync<ConflictException>(() => Service(env, PatientA(env)).RedeemAsync(couponId));
        Assert.Equal(0, await UsedCountAsync(env, couponId));
    }

    [Fact]
    public async Task Inactive_coupon_cannot_be_redeemed()
    {
        using var env = await MedicalTestEnv.CreateAsync();
        var couponId = await AddCouponAsync(env, isActive: false);

        await Assert.ThrowsAsync<ConflictException>(() => Service(env, PatientA(env)).RedeemAsync(couponId));
        Assert.Equal(0, await UsedCountAsync(env, couponId));
    }

    [Fact]
    public async Task Redeeming_an_unknown_coupon_is_a_conflict()
    {
        using var env = await MedicalTestEnv.CreateAsync();

        await Assert.ThrowsAsync<ConflictException>(() => Service(env, PatientA(env)).RedeemAsync(9999));
    }
}

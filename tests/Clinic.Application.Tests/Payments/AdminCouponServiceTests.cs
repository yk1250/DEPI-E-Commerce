using Clinic.Application.Common.Exceptions;
using Clinic.Application.Common.Interfaces;
using Clinic.Application.Common.Security;
using Clinic.Application.Features.Coupons;
using Clinic.Application.Tests.Medical;
using Clinic.Domain.Entities;
using Clinic.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace Clinic.Application.Tests.Payments;

public class AdminCouponServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 7, 10, 0, 0, TimeSpan.Zero);

    private static AdminCouponService Service(MedicalTestEnv env, ICurrentUser user) =>
        new(env.Db, user, new TestClock(Now));

    private static TestCurrentUser Admin() => new() { Role = Roles.Admin };

    private static CreateCouponRequest Create(
        string code = "SUMMER25",
        DiscountType type = DiscountType.Percent,
        decimal value = 25m,
        DateTime? expiresAt = null,
        int maxUses = 50,
        bool isActive = true) =>
        new()
        {
            Code = code,
            DiscountType = type,
            DiscountValue = value,
            ExpiresAt = expiresAt ?? Now.UtcDateTime.AddDays(30),
            MaxUses = maxUses,
            IsActive = isActive
        };

    private static UpdateCouponRequest Update(
        DiscountType type = DiscountType.Fixed,
        decimal value = 40m,
        DateTime? expiresAt = null,
        int maxUses = 80,
        bool isActive = true) =>
        new()
        {
            DiscountType = type,
            DiscountValue = value,
            ExpiresAt = expiresAt ?? Now.UtcDateTime.AddDays(60),
            MaxUses = maxUses,
            IsActive = isActive
        };

    private static async Task<int> AddCouponAsync(MedicalTestEnv env, string code, int usedCount = 0, int maxUses = 50)
    {
        var coupon = new Coupon
        {
            Code = code,
            DiscountType = DiscountType.Percent,
            DiscountValue = 10m,
            ExpiresAt = Now.UtcDateTime.AddDays(30),
            MaxUses = maxUses,
            UsedCount = usedCount,
            IsActive = true
        };
        env.Db.Coupons.Add(coupon);
        await env.Db.SaveChangesAsync();
        return coupon.Id;
    }

    // ---------- الإنشاء ----------

    [Fact]
    public async Task Admin_creates_a_coupon()
    {
        using var env = await MedicalTestEnv.CreateAsync();

        var dto = await Service(env, Admin()).CreateAsync(Create());

        Assert.True(dto.Id > 0);
        Assert.Equal("SUMMER25", dto.Code);
        Assert.Equal(DiscountType.Percent, dto.DiscountType);
        Assert.Equal(25m, dto.DiscountValue);
        Assert.Equal(50, dto.MaxUses);
        Assert.Equal(0, dto.UsedCount);
        Assert.Equal(50, dto.RemainingUses);
        Assert.True(dto.IsActive);
        Assert.False(dto.IsExpired);

        var row = await env.Db.Coupons.AsNoTracking().SingleAsync();
        Assert.Equal("SUMMER25", row.Code);
        Assert.Equal(0, row.UsedCount);
    }

    [Fact]
    public async Task Code_is_trimmed_and_uppercased()
    {
        using var env = await MedicalTestEnv.CreateAsync();

        var dto = await Service(env, Admin()).CreateAsync(Create(code: "  summer-25_x "));

        Assert.Equal("SUMMER-25_X", dto.Code);
    }

    [Theory]
    [InlineData("AB")]
    [InlineData("HAS SPACE")]
    [InlineData("بنسلين")]
    [InlineData("BAD$CODE")]
    [InlineData("")]
    public async Task Invalid_code_is_rejected(string code)
    {
        using var env = await MedicalTestEnv.CreateAsync();

        var ex = await Assert.ThrowsAsync<AppValidationException>(() =>
            Service(env, Admin()).CreateAsync(Create(code: code)));

        Assert.True(ex.Errors.ContainsKey("code"));
        Assert.Equal(0, await env.Db.Coupons.CountAsync());
    }

    [Fact]
    public async Task Code_longer_than_30_characters_is_rejected()
    {
        using var env = await MedicalTestEnv.CreateAsync();

        await Assert.ThrowsAsync<AppValidationException>(() =>
            Service(env, Admin()).CreateAsync(Create(code: new string('A', 31))));
    }

    [Fact]
    public async Task Duplicate_code_is_a_conflict_ignoring_case()
    {
        using var env = await MedicalTestEnv.CreateAsync();
        await AddCouponAsync(env, "SUMMER25");

        await Assert.ThrowsAsync<ConflictException>(() =>
            Service(env, Admin()).CreateAsync(Create(code: "summer25")));

        Assert.Equal(1, await env.Db.Coupons.CountAsync());
    }

    [Fact]
    public async Task Percent_above_100_is_rejected_but_exactly_100_is_allowed()
    {
        using var env = await MedicalTestEnv.CreateAsync();
        var sut = Service(env, Admin());

        var ex = await Assert.ThrowsAsync<AppValidationException>(() =>
            sut.CreateAsync(Create(code: "TOOMUCH", type: DiscountType.Percent, value: 100.01m)));
        Assert.True(ex.Errors.ContainsKey("discountValue"));

        var ok = await sut.CreateAsync(Create(code: "FREE100", type: DiscountType.Percent, value: 100m));
        Assert.Equal(100m, ok.DiscountValue);
    }

    [Fact]
    public async Task Fixed_discount_can_be_a_large_amount()
    {
        using var env = await MedicalTestEnv.CreateAsync();

        var dto = await Service(env, Admin()).CreateAsync(Create(code: "BIG", type: DiscountType.Fixed, value: 5000m));

        Assert.Equal(5000m, dto.DiscountValue);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    public async Task Zero_or_negative_discount_is_rejected(int value)
    {
        using var env = await MedicalTestEnv.CreateAsync();

        var ex = await Assert.ThrowsAsync<AppValidationException>(() =>
            Service(env, Admin()).CreateAsync(Create(value: value)));

        Assert.True(ex.Errors.ContainsKey("discountValue"));
    }

    [Fact]
    public async Task Discount_value_is_rounded_to_two_decimals()
    {
        using var env = await MedicalTestEnv.CreateAsync();

        var dto = await Service(env, Admin()).CreateAsync(Create(type: DiscountType.Fixed, value: 12.345m));

        Assert.Equal(12.35m, dto.DiscountValue);
    }

    [Fact]
    public async Task Discount_that_rounds_to_zero_is_rejected()
    {
        using var env = await MedicalTestEnv.CreateAsync();

        await Assert.ThrowsAsync<AppValidationException>(() =>
            Service(env, Admin()).CreateAsync(Create(type: DiscountType.Fixed, value: 0.001m)));
    }

    [Fact]
    public async Task Expiry_in_the_past_or_now_is_rejected_on_create()
    {
        using var env = await MedicalTestEnv.CreateAsync();
        var sut = Service(env, Admin());

        var ex = await Assert.ThrowsAsync<AppValidationException>(() =>
            sut.CreateAsync(Create(expiresAt: Now.UtcDateTime.AddDays(-1))));
        Assert.True(ex.Errors.ContainsKey("expiresAt"));

        await Assert.ThrowsAsync<AppValidationException>(() =>
            sut.CreateAsync(Create(code: "NOWCODE", expiresAt: Now.UtcDateTime)));
    }

    [Fact]
    public async Task Missing_discount_type_or_expiry_is_rejected()
    {
        using var env = await MedicalTestEnv.CreateAsync();
        var sut = Service(env, Admin());

        await Assert.ThrowsAsync<AppValidationException>(() =>
            sut.CreateAsync(new CreateCouponRequest { Code = "NOTYPE", DiscountValue = 10m, ExpiresAt = Now.UtcDateTime.AddDays(5), MaxUses = 5 }));
        await Assert.ThrowsAsync<AppValidationException>(() =>
            sut.CreateAsync(new CreateCouponRequest { Code = "NOEXPIRY", DiscountType = DiscountType.Percent, DiscountValue = 10m, MaxUses = 5 }));
    }

    [Fact]
    public async Task Max_uses_below_one_is_rejected()
    {
        using var env = await MedicalTestEnv.CreateAsync();

        var ex = await Assert.ThrowsAsync<AppValidationException>(() =>
            Service(env, Admin()).CreateAsync(Create(maxUses: 0)));

        Assert.True(ex.Errors.ContainsKey("maxUses"));
    }

    [Fact]
    public async Task Expiry_is_stored_as_utc_for_unspecified_and_local_values()
    {
        using var env = await MedicalTestEnv.CreateAsync();
        var sut = Service(env, Admin());
        var utc = new DateTime(2026, 12, 31, 12, 0, 0, DateTimeKind.Utc);

        var unspecified = await sut.CreateAsync(Create(code: "UNSPEC", expiresAt: DateTime.SpecifyKind(utc, DateTimeKind.Unspecified)));
        var local = await sut.CreateAsync(Create(code: "LOCAL", expiresAt: utc.ToLocalTime()));

        Assert.Equal(utc, unspecified.ExpiresAt);
        Assert.Equal(DateTimeKind.Utc, unspecified.ExpiresAt.Kind);
        Assert.Equal(utc, local.ExpiresAt);
    }

    [Fact]
    public async Task Coupon_can_be_created_inactive()
    {
        using var env = await MedicalTestEnv.CreateAsync();

        var dto = await Service(env, Admin()).CreateAsync(Create(isActive: false));

        Assert.False(dto.IsActive);
    }

    // ---------- التعديل ----------

    [Fact]
    public async Task Update_changes_the_editable_fields_but_not_the_code_or_used_count()
    {
        using var env = await MedicalTestEnv.CreateAsync();
        var id = await AddCouponAsync(env, "KEEPME", usedCount: 7);

        var dto = await Service(env, Admin()).UpdateAsync(id, Update(DiscountType.Fixed, 40m, maxUses: 80, isActive: false));

        Assert.Equal("KEEPME", dto.Code);
        Assert.Equal(DiscountType.Fixed, dto.DiscountType);
        Assert.Equal(40m, dto.DiscountValue);
        Assert.Equal(80, dto.MaxUses);
        Assert.Equal(7, dto.UsedCount);
        Assert.Equal(73, dto.RemainingUses);
        Assert.False(dto.IsActive);

        var row = await env.Db.Coupons.AsNoTracking().SingleAsync(c => c.Id == id);
        Assert.Equal("KEEPME", row.Code);
        Assert.Equal(7, row.UsedCount);
        Assert.Equal(80, row.MaxUses);
    }

    [Fact]
    public async Task Max_uses_below_the_used_count_is_rejected_but_equal_is_allowed()
    {
        using var env = await MedicalTestEnv.CreateAsync();
        var id = await AddCouponAsync(env, "USED5", usedCount: 5);
        var sut = Service(env, Admin());

        var ex = await Assert.ThrowsAsync<AppValidationException>(() => sut.UpdateAsync(id, Update(maxUses: 4)));
        Assert.True(ex.Errors.ContainsKey("maxUses"));

        var ok = await sut.UpdateAsync(id, Update(maxUses: 5));
        Assert.Equal(0, ok.RemainingUses);
    }

    [Fact]
    public async Task Update_rejects_percent_above_100()
    {
        using var env = await MedicalTestEnv.CreateAsync();
        var id = await AddCouponAsync(env, "PCT");

        var ex = await Assert.ThrowsAsync<AppValidationException>(() =>
            Service(env, Admin()).UpdateAsync(id, Update(DiscountType.Percent, 150m)));

        Assert.True(ex.Errors.ContainsKey("discountValue"));
    }

    [Fact]
    public async Task Update_allows_an_expiry_in_the_past_which_expires_the_coupon_immediately()
    {
        using var env = await MedicalTestEnv.CreateAsync();
        var id = await AddCouponAsync(env, "EXPIRE");

        var dto = await Service(env, Admin()).UpdateAsync(id, Update(expiresAt: Now.UtcDateTime.AddDays(-1)));

        Assert.True(dto.IsExpired);
    }

    [Fact]
    public async Task Update_of_a_missing_coupon_is_not_found()
    {
        using var env = await MedicalTestEnv.CreateAsync();

        await Assert.ThrowsAsync<NotFoundException>(() => Service(env, Admin()).UpdateAsync(9999, Update()));
    }

    [Fact]
    public async Task Update_without_required_fields_is_rejected()
    {
        using var env = await MedicalTestEnv.CreateAsync();
        var id = await AddCouponAsync(env, "REQ");
        var sut = Service(env, Admin());

        await Assert.ThrowsAsync<AppValidationException>(() =>
            sut.UpdateAsync(id, new UpdateCouponRequest { DiscountValue = 10m, ExpiresAt = Now.UtcDateTime.AddDays(5), MaxUses = 5, IsActive = true }));
        await Assert.ThrowsAsync<AppValidationException>(() =>
            sut.UpdateAsync(id, new UpdateCouponRequest { DiscountType = DiscountType.Fixed, DiscountValue = 10m, MaxUses = 5, IsActive = true }));
        await Assert.ThrowsAsync<AppValidationException>(() =>
            sut.UpdateAsync(id, new UpdateCouponRequest { DiscountType = DiscountType.Fixed, DiscountValue = 10m, ExpiresAt = Now.UtcDateTime.AddDays(5), MaxUses = 5 }));
    }

    // ---------- الحذف ----------

    [Fact]
    public async Task Unused_coupon_is_deleted()
    {
        using var env = await MedicalTestEnv.CreateAsync();
        var id = await AddCouponAsync(env, "DELME");

        await Service(env, Admin()).DeleteAsync(id);

        Assert.Equal(0, await env.Db.Coupons.AsNoTracking().CountAsync());
    }

    [Fact]
    public async Task Used_coupon_cannot_be_deleted()
    {
        using var env = await MedicalTestEnv.CreateAsync();
        var id = await AddCouponAsync(env, "USEDONE", usedCount: 1);

        await Assert.ThrowsAsync<ConflictException>(() => Service(env, Admin()).DeleteAsync(id));

        Assert.Equal(1, await env.Db.Coupons.AsNoTracking().CountAsync());
    }

    [Fact]
    public async Task Coupon_referenced_by_a_payment_cannot_be_deleted_even_with_zero_used_count()
    {
        using var env = await MedicalTestEnv.CreateAsync();
        var couponId = await AddCouponAsync(env, "PAYREF", usedCount: 0);
        var appointmentId = await env.AddAppointmentAsync(env.Doctor1, env.PatientA, AppointmentStatus.Confirmed);
        env.Db.Payments.Add(new Payment
        {
            AppointmentId = appointmentId,
            CouponId = couponId,
            Method = PaymentMethod.AtClinic,
            Status = PaymentStatus.Paid,
            OriginalAmount = 300m,
            DiscountAmount = 30m,
            Amount = 270m,
            CreatedAt = Now.UtcDateTime
        });
        await env.Db.SaveChangesAsync();

        await Assert.ThrowsAsync<ConflictException>(() => Service(env, Admin()).DeleteAsync(couponId));

        Assert.Equal(1, await env.Db.Coupons.AsNoTracking().CountAsync());
    }

    [Fact]
    public async Task Delete_of_a_missing_coupon_is_not_found()
    {
        using var env = await MedicalTestEnv.CreateAsync();

        await Assert.ThrowsAsync<NotFoundException>(() => Service(env, Admin()).DeleteAsync(9999));
    }

    // ---------- القراءة ----------

    [Fact]
    public async Task List_returns_newest_first_with_expiry_and_remaining_uses()
    {
        using var env = await MedicalTestEnv.CreateAsync();
        var first = await AddCouponAsync(env, "FIRST", usedCount: 3, maxUses: 10);
        var second = await AddCouponAsync(env, "SECOND");
        var coupon = await env.Db.Coupons.SingleAsync(c => c.Id == first);
        coupon.ExpiresAt = Now.UtcDateTime.AddDays(-1);
        await env.Db.SaveChangesAsync();

        var list = await Service(env, Admin()).ListAsync();

        Assert.Equal(new[] { second, first }, list.Select(c => c.Id).ToArray());
        var firstDto = list.Single(c => c.Id == first);
        Assert.True(firstDto.IsExpired);
        Assert.Equal(7, firstDto.RemainingUses);
        Assert.False(list.Single(c => c.Id == second).IsExpired);
    }

    [Fact]
    public async Task Get_returns_one_coupon_and_unknown_id_is_not_found()
    {
        using var env = await MedicalTestEnv.CreateAsync();
        var id = await AddCouponAsync(env, "ONE");
        var sut = Service(env, Admin());

        var dto = await sut.GetAsync(id);

        Assert.Equal("ONE", dto.Code);
        await Assert.ThrowsAsync<NotFoundException>(() => sut.GetAsync(9999));
    }

    // ---------- الصلاحيات ----------

    [Theory]
    [InlineData(Roles.Patient)]
    [InlineData(Roles.Doctor)]
    [InlineData(Roles.Receptionist)]
    public async Task Only_admins_can_manage_coupons(string role)
    {
        using var env = await MedicalTestEnv.CreateAsync();
        var id = await AddCouponAsync(env, "LOCKED");
        var sut = Service(env, new TestCurrentUser { Role = role, PatientId = env.PatientA, DoctorId = env.Doctor1, ClinicId = env.ClinicId });

        await Assert.ThrowsAsync<ForbiddenException>(() => sut.ListAsync());
        await Assert.ThrowsAsync<ForbiddenException>(() => sut.GetAsync(id));
        await Assert.ThrowsAsync<ForbiddenException>(() => sut.CreateAsync(Create(code: "NEWONE")));
        await Assert.ThrowsAsync<ForbiddenException>(() => sut.UpdateAsync(id, Update()));
        await Assert.ThrowsAsync<ForbiddenException>(() => sut.DeleteAsync(id));

        Assert.Equal(1, await env.Db.Coupons.AsNoTracking().CountAsync());
    }

    [Fact]
    public async Task Unauthenticated_calls_get_401()
    {
        using var env = await MedicalTestEnv.CreateAsync();
        var sut = Service(env, new TestCurrentUser { IsAuthenticated = false });

        await Assert.ThrowsAsync<UnauthorizedException>(() => sut.ListAsync());
        await Assert.ThrowsAsync<UnauthorizedException>(() => sut.CreateAsync(Create()));
    }
}

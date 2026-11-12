using Clinic.Application.Features.Coupons;
using Clinic.Domain.Enums;

namespace Clinic.Application.Tests.Payments;

public class CouponCalculatorTests
{
    [Theory]
    [InlineData(DiscountType.Percent, 10, 300, 30)]
    [InlineData(DiscountType.Percent, 50, 200, 100)]
    [InlineData(DiscountType.Fixed, 50, 300, 50)]
    public void Discount_is_calculated_from_type_and_value(DiscountType type, int value, int original, int expected)
    {
        Assert.Equal((decimal)expected, CouponCalculator.Discount(type, value, original));
    }

    [Fact]
    public void Percent_discount_is_rounded_to_two_decimals_away_from_zero()
    {
        // 15% من 99.99 = 14.9985 -> 15.00
        Assert.Equal(15.00m, CouponCalculator.Discount(DiscountType.Percent, 15m, 99.99m));
        // 12.5% من 10.01 = 1.251250 -> 1.25
        Assert.Equal(1.25m, CouponCalculator.Discount(DiscountType.Percent, 12.5m, 10.01m));
        // 12.5% من 0.12 = 0.015 -> 0.02 (نص الجنيه بيتقرّب لفوق)
        Assert.Equal(0.02m, CouponCalculator.Discount(DiscountType.Percent, 12.5m, 0.12m));
    }

    [Fact]
    public void Fixed_discount_never_exceeds_the_original_amount()
    {
        Assert.Equal(300m, CouponCalculator.Discount(DiscountType.Fixed, 500m, 300m));
    }

    [Fact]
    public void Percent_above_100_is_capped_at_100()
    {
        Assert.Equal(200m, CouponCalculator.Discount(DiscountType.Percent, 150m, 200m));
    }

    [Theory]
    [InlineData(0, 100)]
    [InlineData(-5, 100)]
    [InlineData(10, 0)]
    [InlineData(10, -50)]
    public void Zero_or_negative_inputs_give_no_discount(int value, int original)
    {
        Assert.Equal(0m, CouponCalculator.Discount(DiscountType.Percent, value, original));
        Assert.Equal(0m, CouponCalculator.Discount(DiscountType.Fixed, value, original));
    }

    [Fact]
    public void Unknown_discount_type_gives_no_discount()
    {
        Assert.Equal(0m, CouponCalculator.Discount((DiscountType)99, 10m, 300m));
    }

    [Theory]
    [InlineData(300, 30, 270)]
    [InlineData(300, 300, 0)]
    [InlineData(300, 500, 0)]
    [InlineData(300, 0, 300)]
    public void Final_amount_is_never_negative(int original, int discount, int expected)
    {
        Assert.Equal((decimal)expected, CouponCalculator.FinalAmount(original, discount));
    }
}

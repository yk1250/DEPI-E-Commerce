using Clinic.Domain.Enums;

namespace Clinic.Application.Features.Coupons;

/// حساب الخصم. دوال بحتة (من غير داتابيز) عشان تتختبر بسهولة.
public static class CouponCalculator
{
    /// قيمة الخصم بالجنيه، بتقريب لأقرب قرشين، ومش بتزيد أبداً عن المبلغ الأصلي.
    /// Percent: النسبة بتتقصّ على 100%. Fixed: مبلغ ثابت.
    public static decimal Discount(DiscountType type, decimal value, decimal originalAmount)
    {
        if (originalAmount <= 0m || value <= 0m)
        {
            return 0m;
        }

        var discount = type switch
        {
            DiscountType.Percent => originalAmount * Math.Min(value, 100m) / 100m,
            DiscountType.Fixed => value,
            _ => 0m
        };

        discount = Math.Round(discount, 2, MidpointRounding.AwayFromZero);
        return Math.Min(discount, originalAmount);
    }

    /// Amount = max(0, OriginalAmount - DiscountAmount)
    public static decimal FinalAmount(decimal originalAmount, decimal discountAmount) =>
        Math.Max(0m, originalAmount - discountAmount);
}

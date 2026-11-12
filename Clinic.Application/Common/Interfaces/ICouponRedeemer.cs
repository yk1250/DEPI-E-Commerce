namespace Clinic.Application.Common.Interfaces;

/// استهلاك استخدام واحد من الكوبون بشكل ذري (آمن من التزامن).
public interface ICouponRedeemer
{
    /// بيزوّد UsedCount بواحد في أمر SQL واحد بشرط: الكوبون فعّال، ما انتهاش، وUsedCount < MaxUses.
    /// بيرجّع false لو الشرط ما اتحققش (اتستهلك آخر استخدام، أو انتهى، أو اتعطّل).
    Task<bool> TryRedeemAsync(int couponId, DateTime nowUtc, CancellationToken ct = default);
}

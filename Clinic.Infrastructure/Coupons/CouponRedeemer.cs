using Clinic.Application.Common.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace Clinic.Infrastructure.Coupons;

/// أمر UPDATE واحد بشرط، فالتزامن آمن: لو اتنين حاولوا ياخدوا آخر استخدام، واحد بس بينجح.
///     UPDATE Coupons SET UsedCount = UsedCount + 1
///     WHERE Id = @id AND IsActive = 1 AND ExpiresAt > @now AND UsedCount < MaxUses
public sealed class CouponRedeemer : ICouponRedeemer
{
    private readonly IAppDbContext _db;

    public CouponRedeemer(IAppDbContext db)
    {
        _db = db;
    }

    public async Task<bool> TryRedeemAsync(int couponId, DateTime nowUtc, CancellationToken ct = default)
    {
        var rows = await _db.Coupons
            .Where(c => c.Id == couponId
                        && c.IsActive
                        && c.ExpiresAt > nowUtc
                        && c.UsedCount < c.MaxUses)
            .ExecuteUpdateAsync(s => s.SetProperty(c => c.UsedCount, c => c.UsedCount + 1), ct);

        return rows == 1;
    }
}

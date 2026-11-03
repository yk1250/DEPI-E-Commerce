using Clinic.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Clinic.Infrastructure.Persistence.Configurations;

public class PaymentConfiguration : IEntityTypeConfiguration<Payment>
{
    public void Configure(EntityTypeBuilder<Payment> builder)
    {
        builder.HasIndex(p => p.AppointmentId).IsUnique();
        builder.HasOne<Appointment>().WithMany().HasForeignKey(p => p.AppointmentId);
        builder.HasOne<Coupon>().WithMany().HasForeignKey(p => p.CouponId).IsRequired(false);

        builder.Property(p => p.OriginalAmount).HasPrecision(10, 2);
        builder.Property(p => p.DiscountAmount).HasPrecision(10, 2);
        builder.Property(p => p.Amount).HasPrecision(10, 2);
        builder.Property(p => p.PaymobOrderId).HasMaxLength(64);
        builder.Property(p => p.TransactionId).HasMaxLength(64);
    }
}

using Clinic.Domain.Entities;
using Clinic.Infrastructure.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Clinic.Infrastructure.Persistence.Configurations;

public class NotificationConfiguration : IEntityTypeConfiguration<Notification>
{
    public void Configure(EntityTypeBuilder<Notification> builder)
    {
        builder.HasOne<ApplicationUser>().WithMany().HasForeignKey(n => n.UserId);
        builder.HasOne<Appointment>().WithMany().HasForeignKey(n => n.AppointmentId).IsRequired(false);

        builder.Property(n => n.Title).HasMaxLength(150);
        builder.Property(n => n.Body).HasMaxLength(500);
    }
}

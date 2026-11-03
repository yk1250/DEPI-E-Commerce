using Clinic.Domain.Entities;
using Clinic.Infrastructure.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Clinic.Infrastructure.Persistence.Configurations;

public class DoctorConfiguration : IEntityTypeConfiguration<Doctor>
{
    public void Configure(EntityTypeBuilder<Doctor> builder)
    {
        builder.HasIndex(d => d.UserId).IsUnique();
        builder.HasOne<ApplicationUser>().WithMany().HasForeignKey(d => d.UserId);
        builder.HasOne(d => d.Specialty).WithMany().HasForeignKey(d => d.SpecialtyId);
        builder.HasOne(d => d.Clinic).WithMany().HasForeignKey(d => d.ClinicId);

        builder.Property(d => d.ConsultationFee).HasPrecision(10, 2);
        builder.Property(d => d.FollowUpFeePercent).HasPrecision(5, 2);
        builder.Property(d => d.AverageRating).HasPrecision(3, 2);
    }
}

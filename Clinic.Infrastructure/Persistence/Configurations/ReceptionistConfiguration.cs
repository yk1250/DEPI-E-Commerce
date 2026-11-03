using Clinic.Domain.Entities;
using Clinic.Infrastructure.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Clinic.Infrastructure.Persistence.Configurations;

public class ReceptionistConfiguration : IEntityTypeConfiguration<Receptionist>
{
    public void Configure(EntityTypeBuilder<Receptionist> builder)
    {
        builder.HasIndex(r => r.UserId).IsUnique();
        builder.HasOne<ApplicationUser>().WithMany().HasForeignKey(r => r.UserId);
        builder.HasOne<MedicalClinic>().WithMany().HasForeignKey(r => r.ClinicId);
    }
}

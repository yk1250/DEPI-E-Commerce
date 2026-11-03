using Clinic.Domain.Entities;
using Clinic.Infrastructure.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Clinic.Infrastructure.Persistence.Configurations;

public class PatientConfiguration : IEntityTypeConfiguration<Patient>
{
    public void Configure(EntityTypeBuilder<Patient> builder)
    {
        builder.HasIndex(p => p.UserId).IsUnique();
        builder.HasOne<ApplicationUser>().WithMany().HasForeignKey(p => p.UserId);
    }
}

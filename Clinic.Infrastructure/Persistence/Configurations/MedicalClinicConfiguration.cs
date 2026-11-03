using Clinic.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Clinic.Infrastructure.Persistence.Configurations;

public class MedicalClinicConfiguration : IEntityTypeConfiguration<MedicalClinic>
{
    public void Configure(EntityTypeBuilder<MedicalClinic> builder)
    {
        builder.Property(c => c.Name).HasMaxLength(150);
        builder.Property(c => c.Address).HasMaxLength(300);
        builder.Property(c => c.City).HasMaxLength(80);
        builder.Property(c => c.Phone).HasMaxLength(20);
    }
}

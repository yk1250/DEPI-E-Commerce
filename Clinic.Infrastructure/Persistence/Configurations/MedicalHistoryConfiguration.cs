using Clinic.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Clinic.Infrastructure.Persistence.Configurations;

// الحقول المشفرة (ChronicConditions وAllergies وCurrentMedications) هتتشفر في Infrastructure في مرحلة السجل الطبي
public class MedicalHistoryConfiguration : IEntityTypeConfiguration<MedicalHistory>
{
    public void Configure(EntityTypeBuilder<MedicalHistory> builder)
    {
        builder.HasIndex(m => m.PatientId).IsUnique();
        builder.HasOne<Patient>().WithMany().HasForeignKey(m => m.PatientId);
    }
}

using Clinic.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Clinic.Infrastructure.Persistence.Configurations;

// ملاحظة: ربط قائمة الانتظار بالميعاد المفتوح لسه قرار مفتوح، فالـ configuration ده على الحقول الحالية بس
public class WaitlistEntryConfiguration : IEntityTypeConfiguration<WaitlistEntry>
{
    public void Configure(EntityTypeBuilder<WaitlistEntry> builder)
    {
        builder.HasOne<Doctor>().WithMany().HasForeignKey(w => w.DoctorId);
        builder.HasOne<Patient>().WithMany().HasForeignKey(w => w.PatientId);
    }
}

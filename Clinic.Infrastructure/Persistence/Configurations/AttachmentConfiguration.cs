using Clinic.Domain.Entities;
using Clinic.Infrastructure.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Clinic.Infrastructure.Persistence.Configurations;

public class AttachmentConfiguration : IEntityTypeConfiguration<Attachment>
{
    public void Configure(EntityTypeBuilder<Attachment> builder)
    {
        builder.HasOne<Patient>().WithMany().HasForeignKey(a => a.PatientId);
        builder.HasOne<Appointment>().WithMany().HasForeignKey(a => a.AppointmentId).IsRequired(false);
        builder.HasOne<ApplicationUser>().WithMany().HasForeignKey(a => a.UploadedByUserId);

        builder.Property(a => a.FileName).HasMaxLength(255);
        builder.Property(a => a.StoredPath).HasMaxLength(500);
        builder.Property(a => a.ContentType).HasMaxLength(100);
    }
}

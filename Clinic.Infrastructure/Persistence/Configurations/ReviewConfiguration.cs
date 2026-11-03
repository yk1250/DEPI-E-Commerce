using Clinic.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Clinic.Infrastructure.Persistence.Configurations;

public class ReviewConfiguration : IEntityTypeConfiguration<Review>
{
    public void Configure(EntityTypeBuilder<Review> builder)
    {
        // تقييم واحد لكل موعد
        builder.HasIndex(r => r.AppointmentId).IsUnique();
        builder.HasOne<Appointment>().WithMany().HasForeignKey(r => r.AppointmentId);
        builder.HasOne<Doctor>().WithMany().HasForeignKey(r => r.DoctorId);
        builder.HasOne<Patient>().WithMany().HasForeignKey(r => r.PatientId);

        builder.Property(r => r.Comment).HasMaxLength(1000);
        builder.ToTable(t => t.HasCheckConstraint("CK_Review_Rating", "[Rating] BETWEEN 1 AND 5"));
    }
}

using Clinic.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Clinic.Infrastructure.Persistence.Configurations;

public class AppointmentConfiguration : IEntityTypeConfiguration<Appointment>
{
    public void Configure(EntityTypeBuilder<Appointment> builder)
    {
        builder.HasOne<Doctor>().WithMany().HasForeignKey(a => a.DoctorId);
        builder.HasOne<Patient>().WithMany().HasForeignKey(a => a.PatientId);
        builder.HasOne<Appointment>().WithMany().HasForeignKey(a => a.PreviousAppointmentId).IsRequired(false);

        builder.Property(a => a.PriceSnapshot).HasPrecision(10, 2);
        builder.Property(a => a.CancelReason).HasMaxLength(300);

        // EF بيستخدمه للتحقق من التزامن عند الحجز
        builder.Property(a => a.RowVersion).IsRowVersion();

        builder.HasIndex(a => new { a.DoctorId, a.StartAt })
    
            .IsUnique()
     
            .HasFilter("[Status] <> 4 AND [Status] <> 6");  
    }
}

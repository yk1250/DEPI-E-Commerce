using Clinic.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Clinic.Infrastructure.Persistence.Configurations;

public class ConversationConfiguration : IEntityTypeConfiguration<Conversation>
{
    public void Configure(EntityTypeBuilder<Conversation> builder)
    {
        builder.HasIndex(c => c.AppointmentId).IsUnique();
        builder.HasOne<Appointment>().WithMany().HasForeignKey(c => c.AppointmentId);
        builder.HasOne<Doctor>().WithMany().HasForeignKey(c => c.DoctorId);
        builder.HasOne<Patient>().WithMany().HasForeignKey(c => c.PatientId);
    }
}

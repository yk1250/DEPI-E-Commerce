using Clinic.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Clinic.Infrastructure.Persistence.Configurations;

public class WorkingScheduleConfiguration : IEntityTypeConfiguration<WorkingSchedule>
{
    public void Configure(EntityTypeBuilder<WorkingSchedule> builder)
    {
        builder.HasOne<Doctor>().WithMany().HasForeignKey(w => w.DoctorId);
    }
}

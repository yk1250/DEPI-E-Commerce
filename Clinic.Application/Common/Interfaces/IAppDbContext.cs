using Clinic.Application.Common.Models;
using Clinic.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace Clinic.Application.Common.Interfaces;

public interface IAppDbContext
{
    DbSet<Specialty> Specialties { get; }
    DbSet<MedicalClinic> Clinics { get; }
    DbSet<Doctor> Doctors { get; }
    DbSet<Patient> Patients { get; }
    DbSet<Receptionist> Receptionists { get; }
    DbSet<WorkingSchedule> WorkingSchedules { get; }
    DbSet<TimeOff> TimeOffs { get; }
    DbSet<Appointment> Appointments { get; }
    DbSet<WaitlistEntry> WaitlistEntries { get; }
    DbSet<Coupon> Coupons { get; }
    DbSet<Payment> Payments { get; }
    DbSet<MedicalHistory> MedicalHistories { get; }
    DbSet<Attachment> Attachments { get; }
    DbSet<Review> Reviews { get; }
    DbSet<Conversation> Conversations { get; }
    DbSet<Message> Messages { get; }
    DbSet<Notification> Notifications { get; }

    /// قراءة بس: بيانات الحسابات (الاسم والإيميل...). بتتعمل join عليها مع Doctors وPatients.
    IQueryable<UserSummary> UserSummaries { get; }

    DatabaseFacade Database { get; }

    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}
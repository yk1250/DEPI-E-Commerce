using System;
using System.Threading.Tasks;
using Clinic.Application.Features.Appointments;
using Clinic.Domain.Entities;
using Clinic.Domain.Enums;
using Clinic.Infrastructure.Identity;
using Clinic.Infrastructure.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Clinic.Application.Tests.Booking;

public class AppointmentExpiryTests
{
    // الثلاثاء 3 نوفمبر 2026، 9:00 صباحاً بتوقيت القاهرة = 07:00 UTC
    private static readonly DateTime SlotUtc = new(2026, 11, 3, 7, 0, 0, DateTimeKind.Utc);
    private const decimal Fee = 300m;

    private sealed class FixedClock : TimeProvider
    {
        private readonly DateTimeOffset _now;

        public FixedClock(DateTimeOffset now)
        {
            _now = now;
        }

        public override DateTimeOffset GetUtcNow() => _now;
    }

    // نسخة للاختبار بس: SQLite ما بيولّدش RowVersion زي SQL Server
    private sealed class TestDbContext : AppDbContext
    {
        public TestDbContext(DbContextOptions<AppDbContext> options) : base(options)
        {
        }

        protected override void OnModelCreating(ModelBuilder builder)
        {
            base.OnModelCreating(builder);
            builder.Entity<Appointment>().Property(a => a.RowVersion).ValueGeneratedNever();
        }
    }

    private static AppDbContext CreateDb(SqliteConnection connection)
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(connection)
            .Options;

        var db = new TestDbContext(options);
        db.Database.EnsureCreated();
        return db;
    }

    private static async Task<(int DoctorId, int PatientId)> SeedAsync(AppDbContext db)
    {
        var specialty = new Specialty { NameAr = "باطنة", NameEn = "Internal" };
        var clinic = new MedicalClinic { Name = "Test Clinic", Address = "Test", City = "Fayoum" };
        db.Specialties.Add(specialty);
        db.Clinics.Add(clinic);
        await db.SaveChangesAsync();

        var doctorUser = new ApplicationUser
        {
            Id = Guid.NewGuid(),
            UserName = "doctor1",
            Email = "doctor1@test.local",
            FullName = "doctor1"
        };
        var patientUser = new ApplicationUser
        {
            Id = Guid.NewGuid(),
            UserName = "patient1",
            Email = "patient1@test.local",
            FullName = "patient1"
        };
        db.Users.Add(doctorUser);
        db.Users.Add(patientUser);
        await db.SaveChangesAsync();

        var doctor = new Doctor
        {
            UserId = doctorUser.Id,
            SpecialtyId = specialty.Id,
            ClinicId = clinic.Id,
            ConsultationFee = Fee,
            IsApproved = true
        };
        var patient = new Patient
        {
            UserId = patientUser.Id,
            DateOfBirth = new DateTime(1995, 1, 1)
        };
        db.Doctors.Add(doctor);
        db.Patients.Add(patient);
        await db.SaveChangesAsync();

        db.WorkingSchedules.Add(new WorkingSchedule
        {
            DoctorId = doctor.Id,
            DayOfWeek = DayOfWeek.Tuesday,
            StartTime = new TimeOnly(9, 0),
            EndTime = new TimeOnly(12, 0),
            SlotDurationMinutes = 30,
            IsActive = true
        });
        await db.SaveChangesAsync();

        return (doctor.Id, patient.Id);
    }

    [Fact]
    public async Task Expired_pending_payment_becomes_expired_and_others_are_untouched()
    {
        using var connection = new SqliteConnection("DataSource=:memory:");
        connection.Open();
        using var db = CreateDb(connection);
        var (doctorId, patientId) = await SeedAsync(db);

        // وقت الحجز: 1 نوفمبر، قبل الموعد بيومين
        var bookingTime = new DateTimeOffset(2026, 11, 1, 7, 0, 0, TimeSpan.Zero);
        var bookingService = new AppointmentService(db, new FixedClock(bookingTime));

        // حجز أونلاين: ExpiresAt = وقت الحجز + 15 دقيقة
        var pendingExpired = await bookingService.BookAsync(new BookAppointmentRequest(
            patientId, doctorId, SlotUtc, AppointmentType.New, PaymentMethod.Online));

        // حجز أونلاين تاني: ExpiresAt لسه في المستقبل وقت التشغيل
        var pendingFresh = await bookingService.BookAsync(new BookAppointmentRequest(
            patientId, doctorId, SlotUtc.AddMinutes(30), AppointmentType.New, PaymentMethod.Online));

        // حجز في العيادة: Confirmed ما يتلمسش أبداً
        var confirmed = await bookingService.BookAsync(new BookAppointmentRequest(
            patientId, doctorId, SlotUtc.AddMinutes(60), AppointmentType.New, PaymentMethod.AtClinic));

        // نعدّل ExpiresAt للحجز الأول عشان يكون عدّى وقته، وتاني عشان يفضل في المستقبل
        var expiredRow = await db.Appointments.FirstAsync(a => a.Id == pendingExpired.Id);
        expiredRow.ExpiresAt = bookingTime.UtcDateTime.AddMinutes(-1);

        var freshRow = await db.Appointments.FirstAsync(a => a.Id == pendingFresh.Id);
        freshRow.ExpiresAt = bookingTime.UtcDateTime.AddHours(1);

        await db.SaveChangesAsync();

        // وقت التشغيل: بعد الحجز الأول بساعة، قبل ما الـ freshRow ينتهي
        var runnerTime = new DateTimeOffset(2026, 11, 1, 8, 0, 0, TimeSpan.Zero);
        var runner = new AppointmentService(db, new FixedClock(runnerTime));

        var changed = await runner.ExpirePendingAsync();

        Assert.Equal(1, changed);

        var afterExpired = await db.Appointments.AsNoTracking().FirstAsync(a => a.Id == pendingExpired.Id);
        var afterFresh = await db.Appointments.AsNoTracking().FirstAsync(a => a.Id == pendingFresh.Id);
        var afterConfirmed = await db.Appointments.AsNoTracking().FirstAsync(a => a.Id == confirmed.Id);

        Assert.Equal(AppointmentStatus.Expired, afterExpired.Status);
        Assert.Equal(AppointmentStatus.PendingPayment, afterFresh.Status);
        Assert.Equal(AppointmentStatus.Confirmed, afterConfirmed.Status);
    }
}
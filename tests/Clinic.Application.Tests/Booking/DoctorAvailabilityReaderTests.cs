using System;
using System.Threading.Tasks;
using Clinic.Domain.Entities;
using Clinic.Domain.Enums;
using Clinic.Infrastructure.Booking;
using Clinic.Infrastructure.Identity;
using Clinic.Infrastructure.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Clinic.Application.Tests.Booking;

public class DoctorAvailabilityReaderTests
{
    // الثلاثاء 3 نوفمبر 2026
    private static readonly DateOnly Day = new(2026, 11, 3);

    // نسخة للاختبار بس: بتوقف توليد RowVersion لأن SQLite ما بيدعمهوش زي SQL Server
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

    private static async Task<Guid> CreateUserAsync(AppDbContext db, string name)
    {
        var user = new ApplicationUser
        {
            Id = Guid.NewGuid(),
            UserName = name,
            Email = $"{name}@test.local",
            FullName = name
        };
        db.Users.Add(user);
        await db.SaveChangesAsync();
        return user.Id;
    }

    private static async Task<(int DoctorId, int PatientId)> SeedAsync(AppDbContext db)
    {
        var specialty = new Specialty { NameAr = "باطنة", NameEn = "Internal" };
        var clinic = new MedicalClinic { Name = "Test Clinic", Address = "Test", City = "Fayoum" };
        db.Specialties.Add(specialty);
        db.Clinics.Add(clinic);
        await db.SaveChangesAsync();

        var doctorUserId = await CreateUserAsync(db, "doctor1");
        var patientUserId = await CreateUserAsync(db, "patient1");

        var doctor = new Doctor
        {
            UserId = doctorUserId,
            SpecialtyId = specialty.Id,
            ClinicId = clinic.Id,
            IsApproved = true
        };
        var patient = new Patient
        {
            UserId = patientUserId,
            DateOfBirth = new DateTime(1995, 1, 1)
        };
        db.Doctors.Add(doctor);
        db.Patients.Add(patient);
        await db.SaveChangesAsync();

        // دوام الدكتور: الثلاثاء من 9 لـ 10 الصبح بتوقيت القاهرة، والكشف 30 دقيقة
        db.WorkingSchedules.Add(new WorkingSchedule
        {
            DoctorId = doctor.Id,
            DayOfWeek = DayOfWeek.Tuesday,
            StartTime = new TimeOnly(9, 0),
            EndTime = new TimeOnly(10, 0),
            SlotDurationMinutes = 30,
            IsActive = true
        });
        await db.SaveChangesAsync();

        return (doctor.Id, patient.Id);
    }

    private static Appointment MakeAppointment(int doctorId, int patientId, DateTime startUtc, AppointmentStatus status)
    {
        return new Appointment
        {
            DoctorId = doctorId,
            PatientId = patientId,
            StartAt = startUtc,
            EndAt = startUtc.AddMinutes(30),
            Status = status,
            CreatedAt = DateTime.UtcNow,
            RowVersion = new byte[] { 0, 0, 0, 0, 0, 0, 0, 1 }
        };
    }

    [Fact]
    public async Task Booked_appointments_are_read_in_cairo_time_and_cancelled_ones_are_ignored()
    {
        using var connection = new SqliteConnection("DataSource=:memory:");
        connection.Open();
        using var db = CreateDb(connection);
        var (doctorId, patientId) = await SeedAsync(db);

        // 9:00 القاهرة = 07:00 UTC، محجوز فعلاً
        var confirmed = MakeAppointment(
            doctorId, patientId,
            new DateTime(2026, 11, 3, 7, 0, 0, DateTimeKind.Utc),
            AppointmentStatus.Confirmed);

        // 9:30 القاهرة = 07:30 UTC، ملغي، لازم يتتجاهل
        var cancelled = MakeAppointment(
            doctorId, patientId,
            new DateTime(2026, 11, 3, 7, 30, 0, DateTimeKind.Utc),
            AppointmentStatus.Cancelled);

        db.Appointments.Add(confirmed);
        db.Appointments.Add(cancelled);
        await db.SaveChangesAsync();

        var reader = new DoctorAvailabilityReader(db);

        var result = await reader.GetAsync(doctorId, Day);

        Assert.NotNull(result);
        Assert.Equal(new TimeOnly(9, 0), result!.WorkStart);
        Assert.Equal(new TimeOnly(10, 0), result.WorkEnd);
        Assert.Single(result.BookedStarts);
        Assert.Equal(new TimeOnly(9, 0), result.BookedStarts[0]);
    }

    [Fact]
    public async Task Time_off_is_read_in_cairo_time()
    {
        using var connection = new SqliteConnection("DataSource=:memory:");
        connection.Open();
        using var db = CreateDb(connection);
        var (doctorId, _) = await SeedAsync(db);

        // إجازة من 9:00 لـ 9:30 القاهرة = 07:00 لـ 07:30 UTC
        db.TimeOffs.Add(new TimeOff
        {
            DoctorId = doctorId,
            StartAt = new DateTime(2026, 11, 3, 7, 0, 0, DateTimeKind.Utc),
            EndAt = new DateTime(2026, 11, 3, 7, 30, 0, DateTimeKind.Utc)
        });
        await db.SaveChangesAsync();

        var reader = new DoctorAvailabilityReader(db);

        var result = await reader.GetAsync(doctorId, Day);

        Assert.NotNull(result);
        Assert.Single(result!.TimeOffs);
        Assert.Equal(new TimeOnly(9, 0), result.TimeOffs[0].From);
        Assert.Equal(new TimeOnly(9, 30), result.TimeOffs[0].To);
    }
}
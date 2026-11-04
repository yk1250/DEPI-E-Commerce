using System;
using System.Threading.Tasks;
using Clinic.Application.Common.Exceptions;
using Clinic.Application.Features.Appointments;
using Clinic.Domain.Entities;
using Clinic.Domain.Enums;
using Clinic.Infrastructure.Identity;
using Clinic.Infrastructure.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Clinic.Application.Tests.Booking;

public class AppointmentServiceTests
{
    // الثلاثاء 3 نوفمبر 2026، 9:00 صباحاً بتوقيت القاهرة = 07:00 UTC
    private static readonly DateTime SlotUtc = new(2026, 11, 3, 7, 0, 0, DateTimeKind.Utc);
    private const decimal Fee = 300m;

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
            ConsultationFee = Fee,
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

    [Fact]
    public async Task Booking_free_slot_online_returns_pending_payment_with_fee_snapshot()
    {
        using var connection = new SqliteConnection("DataSource=:memory:");
        connection.Open();
        using var db = CreateDb(connection);
        var (doctorId, patientId) = await SeedAsync(db);

        var service = new AppointmentService(db);

        var appointment = await service.BookAsync(new BookAppointmentRequest(
            patientId, doctorId, SlotUtc, AppointmentType.New, PaymentMethod.Online));

        Assert.Equal(AppointmentStatus.PendingPayment, appointment.Status);
        Assert.Equal(Fee, appointment.PriceSnapshot);
        Assert.NotNull(appointment.ExpiresAt);
    }

    [Fact]
    public async Task Booking_same_slot_twice_throws_conflict()
    {
        using var connection = new SqliteConnection("DataSource=:memory:");
        connection.Open();
        using var db = CreateDb(connection);
        var (doctorId, patientId) = await SeedAsync(db);

        var service = new AppointmentService(db);

        await service.BookAsync(new BookAppointmentRequest(
            patientId, doctorId, SlotUtc, AppointmentType.New, PaymentMethod.Online));

        await Assert.ThrowsAsync<ConflictException>(() => service.BookAsync(new BookAppointmentRequest(
            patientId, doctorId, SlotUtc, AppointmentType.New, PaymentMethod.Online)));
    }

    [Fact]
    public async Task Booking_before_working_hours_throws_validation()
    {
        using var connection = new SqliteConnection("DataSource=:memory:");
        connection.Open();
        using var db = CreateDb(connection);
        var (doctorId, patientId) = await SeedAsync(db);

        var service = new AppointmentService(db);

        // 8:00 صباحاً بتوقيت القاهرة = 06:00 UTC، قبل بداية الدوام (9:00)
        var beforeShiftUtc = new DateTime(2026, 11, 3, 6, 0, 0, DateTimeKind.Utc);

        await Assert.ThrowsAsync<AppValidationException>(() => service.BookAsync(new BookAppointmentRequest(
            patientId, doctorId, beforeShiftUtc, AppointmentType.New, PaymentMethod.Online)));
    }

    [Fact]
    public async Task Booking_off_grid_time_throws_validation()
    {
        using var connection = new SqliteConnection("DataSource=:memory:");
        connection.Open();
        using var db = CreateDb(connection);
        var (doctorId, patientId) = await SeedAsync(db);

        var service = new AppointmentService(db);

        // 9:15 صباحاً بتوقيت القاهرة = 07:15 UTC، مش على مضاعفات الـ 30 دقيقة
        var offGridUtc = new DateTime(2026, 11, 3, 7, 15, 0, DateTimeKind.Utc);

        await Assert.ThrowsAsync<AppValidationException>(() => service.BookAsync(new BookAppointmentRequest(
            patientId, doctorId, offGridUtc, AppointmentType.New, PaymentMethod.Online)));
    }

    [Fact]
    public async Task Booking_with_unapproved_doctor_throws_validation()
    {
        using var connection = new SqliteConnection("DataSource=:memory:");
        connection.Open();
        using var db = CreateDb(connection);
        var (doctorId, patientId) = await SeedAsync(db);

        var doctor = await db.Doctors.FirstAsync(d => d.Id == doctorId);
        doctor.IsApproved = false;
        await db.SaveChangesAsync();

        var service = new AppointmentService(db);

        await Assert.ThrowsAsync<AppValidationException>(() => service.BookAsync(new BookAppointmentRequest(
            patientId, doctorId, SlotUtc, AppointmentType.New, PaymentMethod.Online)));
    }

    [Fact]
    public async Task Booking_with_locked_doctor_account_throws_validation()
    {
        using var connection = new SqliteConnection("DataSource=:memory:");
        connection.Open();
        using var db = CreateDb(connection);
        var (doctorId, patientId) = await SeedAsync(db);

        // نقفل حساب الدكتور (الـ IsActive في ApplicationUser مش في Doctor)
        var doctor = await db.Doctors.FirstAsync(d => d.Id == doctorId);
        var user = await db.Users.FirstAsync(u => u.Id == doctor.UserId);
        user.IsActive = false;
        await db.SaveChangesAsync();

        var service = new AppointmentService(db);

        await Assert.ThrowsAsync<AppValidationException>(() => service.BookAsync(new BookAppointmentRequest(
            patientId, doctorId, SlotUtc, AppointmentType.New, PaymentMethod.Online)));
    }
}
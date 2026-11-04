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

    // الساعة قبل الموعد بيوم: الإلغاء مسموح
    private static readonly TimeProvider EarlyClock =
        new FixedClock(new DateTimeOffset(2026, 11, 2, 12, 0, 0, TimeSpan.Zero));

    // الساعة قبل الموعد بساعة واحدة: الإلغاء ممنوع (أقل من ساعتين)
    private static readonly TimeProvider LateClock =
        new FixedClock(new DateTimeOffset(2026, 11, 3, 6, 0, 0, TimeSpan.Zero));

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

    private static async Task<int> AddPatientAsync(AppDbContext db, string name)
    {
        var userId = await CreateUserAsync(db, name);
        var patient = new Patient
        {
            UserId = userId,
            DateOfBirth = new DateTime(1995, 1, 1)
        };
        db.Patients.Add(patient);
        await db.SaveChangesAsync();
        return patient.Id;
    }

    private static async Task<(int DoctorId, int PatientId)> SeedAsync(AppDbContext db)
    {
        var specialty = new Specialty { NameAr = "باطنة", NameEn = "Internal" };
        var clinic = new MedicalClinic { Name = "Test Clinic", Address = "Test", City = "Fayoum" };
        db.Specialties.Add(specialty);
        db.Clinics.Add(clinic);
        await db.SaveChangesAsync();

        var doctorUserId = await CreateUserAsync(db, "doctor1");

        var doctor = new Doctor
        {
            UserId = doctorUserId,
            SpecialtyId = specialty.Id,
            ClinicId = clinic.Id,
            ConsultationFee = Fee,
            IsApproved = true
        };
        db.Doctors.Add(doctor);
        await db.SaveChangesAsync();

        var patientId = await AddPatientAsync(db, "patient1");

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

        return (doctor.Id, patientId);
    }

    [Fact]
    public async Task Booking_free_slot_online_returns_pending_payment_with_fee_snapshot()
    {
        using var connection = new SqliteConnection("DataSource=:memory:");
        connection.Open();
        using var db = CreateDb(connection);
        var (doctorId, patientId) = await SeedAsync(db);

        var service = new AppointmentService(db, EarlyClock);

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

        var service = new AppointmentService(db, EarlyClock);

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

        var service = new AppointmentService(db, EarlyClock);

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

        var service = new AppointmentService(db, EarlyClock);

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

        var service = new AppointmentService(db, EarlyClock);

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

        var doctor = await db.Doctors.FirstAsync(d => d.Id == doctorId);
        var user = await db.Users.FirstAsync(u => u.Id == doctor.UserId);
        user.IsActive = false;
        await db.SaveChangesAsync();

        var service = new AppointmentService(db, EarlyClock);

        await Assert.ThrowsAsync<AppValidationException>(() => service.BookAsync(new BookAppointmentRequest(
            patientId, doctorId, SlotUtc, AppointmentType.New, PaymentMethod.Online)));
    }

    [Fact]
    public async Task Patient_cancels_confirmed_appointment_sets_status_and_reason()
    {
        using var connection = new SqliteConnection("DataSource=:memory:");
        connection.Open();
        using var db = CreateDb(connection);
        var (doctorId, patientId) = await SeedAsync(db);

        var service = new AppointmentService(db, EarlyClock);

        var appointment = await service.BookAsync(new BookAppointmentRequest(
            patientId, doctorId, SlotUtc, AppointmentType.New, PaymentMethod.AtClinic));
        Assert.Equal(AppointmentStatus.Confirmed, appointment.Status);

        var cancelled = await service.CancelByPatientAsync(appointment.Id, patientId, "طارئ");

        Assert.Equal(AppointmentStatus.Cancelled, cancelled.Status);
        Assert.NotNull(cancelled.CancelledAt);
        Assert.Equal("طارئ", cancelled.CancelReason);
    }

    [Fact]
    public async Task After_cancel_the_same_slot_can_be_booked_again()
    {
        using var connection = new SqliteConnection("DataSource=:memory:");
        connection.Open();
        using var db = CreateDb(connection);
        var (doctorId, patientId) = await SeedAsync(db);

        var service = new AppointmentService(db, EarlyClock);

        var first = await service.BookAsync(new BookAppointmentRequest(
            patientId, doctorId, SlotUtc, AppointmentType.New, PaymentMethod.AtClinic));

        await service.CancelByPatientAsync(first.Id, patientId, null);

        var second = await service.BookAsync(new BookAppointmentRequest(
            patientId, doctorId, SlotUtc, AppointmentType.New, PaymentMethod.AtClinic));

        Assert.NotEqual(first.Id, second.Id);
        Assert.Equal(AppointmentStatus.Confirmed, second.Status);
    }

    [Fact]
    public async Task Patient_cannot_cancel_another_patients_appointment()
    {
        using var connection = new SqliteConnection("DataSource=:memory:");
        connection.Open();
        using var db = CreateDb(connection);
        var (doctorId, patientId) = await SeedAsync(db);
        var otherPatientId = await AddPatientAsync(db, "patient2");

        var service = new AppointmentService(db, EarlyClock);

        var appointment = await service.BookAsync(new BookAppointmentRequest(
            patientId, doctorId, SlotUtc, AppointmentType.New, PaymentMethod.AtClinic));

        await Assert.ThrowsAsync<ForbiddenException>(() =>
            service.CancelByPatientAsync(appointment.Id, otherPatientId, null));
    }

    [Fact]
    public async Task Patient_cannot_cancel_less_than_two_hours_before_start()
    {
        using var connection = new SqliteConnection("DataSource=:memory:");
        connection.Open();
        using var db = CreateDb(connection);
        var (doctorId, patientId) = await SeedAsync(db);

        // الحجز اتعمل وقت الساعة قبل الموعد بيوم
        var bookingService = new AppointmentService(db, EarlyClock);
        var appointment = await bookingService.BookAsync(new BookAppointmentRequest(
            patientId, doctorId, SlotUtc, AppointmentType.New, PaymentMethod.AtClinic));

        // الإلغاء بيحصل وقت الساعة قبل الموعد بساعة واحدة
        var cancelService = new AppointmentService(db, LateClock);

        await Assert.ThrowsAsync<ConflictException>(() =>
            cancelService.CancelByPatientAsync(appointment.Id, patientId, null));
    }
}
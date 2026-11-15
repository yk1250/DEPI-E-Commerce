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

public class AppointmentStatusTests
{
    // الثلاثاء 3 نوفمبر 2026، 9:00 صباحاً بتوقيت القاهرة = 07:00 UTC
    private static readonly DateTime SlotUtc = new(2026, 11, 3, 7, 0, 0, DateTimeKind.Utc);

    // وقت الحجز: قبل الموعد بيومين (لازم الحجز يحصل قبل الموعد)
    private static readonly DateTimeOffset BookingTime = new(2026, 11, 1, 7, 0, 0, TimeSpan.Zero);

    // قبل بداية الموعد بنص ساعة
    private static readonly DateTimeOffset BeforeStart = new(2026, 11, 3, 6, 30, 0, TimeSpan.Zero);

    // بعد بداية الموعد بساعة
    private static readonly DateTimeOffset AfterStart = new(2026, 11, 3, 8, 0, 0, TimeSpan.Zero);

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
            ConsultationFee = 300m,
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
            EndTime = new TimeOnly(10, 0),
            SlotDurationMinutes = 30,
            IsActive = true
        });
        await db.SaveChangesAsync();

        return (doctor.Id, patient.Id);
    }

    // حجز في العيادة: الحالة Confirmed فوراً، والحجز بيحصل وقت BookingTime (قبل الموعد)
    private static Task<Appointment> BookConfirmedAsync(AppDbContext db, int doctorId, int patientId)
    {
        var bookingService = new AppointmentService(db, new FixedClock(BookingTime));
        return bookingService.BookAsync(new BookAppointmentRequest(
            patientId, doctorId, SlotUtc, AppointmentType.New, PaymentMethod.AtClinic));
    }

    [Fact]
    public async Task Check_in_confirmed_appointment_moves_to_checked_in()
    {
        using var connection = new SqliteConnection("DataSource=:memory:");
        connection.Open();
        using var db = CreateDb(connection);
        var (doctorId, patientId) = await SeedAsync(db);

        var appointment = await BookConfirmedAsync(db, doctorId, patientId);

        var service = new AppointmentService(db, new FixedClock(BookingTime));
        var result = await service.CheckInAsync(appointment.Id);

        Assert.Equal(AppointmentStatus.CheckedIn, result.Status);
    }

    [Fact]
    public async Task Complete_without_check_in_throws_conflict()
    {
        using var connection = new SqliteConnection("DataSource=:memory:");
        connection.Open();
        using var db = CreateDb(connection);
        var (doctorId, patientId) = await SeedAsync(db);

        var appointment = await BookConfirmedAsync(db, doctorId, patientId);

        var service = new AppointmentService(db, new FixedClock(AfterStart));

        await Assert.ThrowsAsync<ConflictException>(() => service.CompleteAsync(appointment.Id));
    }

    [Fact]
    public async Task Check_in_then_complete_moves_to_completed()
    {
        using var connection = new SqliteConnection("DataSource=:memory:");
        connection.Open();
        using var db = CreateDb(connection);
        var (doctorId, patientId) = await SeedAsync(db);

        var appointment = await BookConfirmedAsync(db, doctorId, patientId);

        var checkInService = new AppointmentService(db, new FixedClock(BookingTime));
        await checkInService.CheckInAsync(appointment.Id);

        var completeService = new AppointmentService(db, new FixedClock(AfterStart));
        var result = await completeService.CompleteAsync(appointment.Id);

        Assert.Equal(AppointmentStatus.Completed, result.Status);
    }

    [Fact]
    public async Task No_show_before_start_time_throws_conflict()
    {
        using var connection = new SqliteConnection("DataSource=:memory:");
        connection.Open();
        using var db = CreateDb(connection);
        var (doctorId, patientId) = await SeedAsync(db);

        var appointment = await BookConfirmedAsync(db, doctorId, patientId);

        var service = new AppointmentService(db, new FixedClock(BeforeStart));

        await Assert.ThrowsAsync<ConflictException>(() => service.MarkNoShowAsync(appointment.Id));
    }

    [Fact]
    public async Task No_show_after_start_time_marks_no_show()
    {
        using var connection = new SqliteConnection("DataSource=:memory:");
        connection.Open();
        using var db = CreateDb(connection);
        var (doctorId, patientId) = await SeedAsync(db);

        var appointment = await BookConfirmedAsync(db, doctorId, patientId);

        var service = new AppointmentService(db, new FixedClock(AfterStart));
        var result = await service.MarkNoShowAsync(appointment.Id);

        Assert.Equal(AppointmentStatus.NoShow, result.Status);
    }

    [Fact]
    public async Task Check_in_cancelled_appointment_throws_conflict()
    {
        using var connection = new SqliteConnection("DataSource=:memory:");
        connection.Open();
        using var db = CreateDb(connection);
        var (doctorId, patientId) = await SeedAsync(db);

        var appointment = await BookConfirmedAsync(db, doctorId, patientId);

        var cancelService = new AppointmentService(db, new FixedClock(BookingTime));
        await cancelService.CancelByPatientAsync(appointment.Id, patientId, null);

        await Assert.ThrowsAsync<ConflictException>(() => cancelService.CheckInAsync(appointment.Id));
    }
}
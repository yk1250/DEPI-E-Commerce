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

public class AppointmentDoctorCancelTests
{
    // الثلاثاء 3 نوفمبر 2026، 9:00 صباحاً بتوقيت القاهرة = 07:00 UTC
    private static readonly DateTime SlotUtc = new(2026, 11, 3, 7, 0, 0, DateTimeKind.Utc);

    // وقت الحجز: قبل الموعد بيومين
    private static readonly DateTimeOffset BookingTime = new(2026, 11, 1, 7, 0, 0, TimeSpan.Zero);

    // قبل الموعد بساعة واحدة: أقل من ساعتين، والدكتور لازم يقدر يلغي برضه
    private static readonly DateTimeOffset LessThanTwoHoursBefore = new(2026, 11, 3, 6, 0, 0, TimeSpan.Zero);

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

    [Fact]
    public async Task Doctor_cancels_confirmed_appointment_sets_status_and_reason()
    {
        using var connection = new SqliteConnection("DataSource=:memory:");
        connection.Open();
        using var db = CreateDb(connection);
        var (doctorId, patientId) = await SeedAsync(db);

        var booking = new AppointmentService(db, new FixedClock(BookingTime));
        var appointment = await booking.BookAsync(new BookAppointmentRequest(
            patientId, doctorId, SlotUtc, AppointmentType.New, PaymentMethod.AtClinic));

        var result = await booking.CancelByDoctorAsync(appointment.Id, doctorId, "طارئ عند الدكتور");

        Assert.Equal(AppointmentStatus.Cancelled, result.Status);
        Assert.NotNull(result.CancelledAt);
        Assert.Equal("طارئ عند الدكتور", result.CancelReason);
    }

    [Fact]
    public async Task Doctor_can_cancel_less_than_two_hours_before_start()
    {
        using var connection = new SqliteConnection("DataSource=:memory:");
        connection.Open();
        using var db = CreateDb(connection);
        var (doctorId, patientId) = await SeedAsync(db);

        var booking = new AppointmentService(db, new FixedClock(BookingTime));
        var appointment = await booking.BookAsync(new BookAppointmentRequest(
            patientId, doctorId, SlotUtc, AppointmentType.New, PaymentMethod.AtClinic));

        var lateCancel = new AppointmentService(db, new FixedClock(LessThanTwoHoursBefore));
        var result = await lateCancel.CancelByDoctorAsync(appointment.Id, doctorId, null);

        Assert.Equal(AppointmentStatus.Cancelled, result.Status);
    }

    [Fact]
    public async Task Doctor_cannot_cancel_another_doctors_appointment()
    {
        using var connection = new SqliteConnection("DataSource=:memory:");
        connection.Open();
        using var db = CreateDb(connection);
        var (doctorId, patientId) = await SeedAsync(db);

        var booking = new AppointmentService(db, new FixedClock(BookingTime));
        var appointment = await booking.BookAsync(new BookAppointmentRequest(
            patientId, doctorId, SlotUtc, AppointmentType.New, PaymentMethod.AtClinic));

        var otherDoctorId = doctorId + 1;

        await Assert.ThrowsAsync<ForbiddenException>(() =>
            booking.CancelByDoctorAsync(appointment.Id, otherDoctorId, null));
    }

    [Fact]
    public async Task Doctor_cannot_cancel_completed_appointment()
    {
        using var connection = new SqliteConnection("DataSource=:memory:");
        connection.Open();
        using var db = CreateDb(connection);
        var (doctorId, patientId) = await SeedAsync(db);

        var service = new AppointmentService(db, new FixedClock(BookingTime));
        var appointment = await service.BookAsync(new BookAppointmentRequest(
            patientId, doctorId, SlotUtc, AppointmentType.New, PaymentMethod.AtClinic));

        await service.CheckInAsync(appointment.Id);
        await service.CompleteAsync(appointment.Id);

        await Assert.ThrowsAsync<ConflictException>(() =>
            service.CancelByDoctorAsync(appointment.Id, doctorId, null));
    }
}
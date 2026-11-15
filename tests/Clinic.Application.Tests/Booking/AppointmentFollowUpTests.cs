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

public class AppointmentFollowUpTests
{
    // الثلاثاء 3 نوفمبر 2026، 9:00 صباحاً بتوقيت القاهرة = 07:00 UTC
    private static readonly DateTime FirstSlotUtc = new(2026, 11, 3, 7, 0, 0, DateTimeKind.Utc);

    // متابعة بعد 7 أيام من الكشف الأول (داخل المدة 14 يوم)
    private static readonly DateTime FollowUpWithinUtc = new(2026, 11, 10, 7, 0, 0, DateTimeKind.Utc);

    // متابعة بعد 21 يوم من الكشف الأول (برا المدة 14 يوم)
    private static readonly DateTime FollowUpOutsideUtc = new(2026, 11, 24, 7, 0, 0, DateTimeKind.Utc);

    // وقت الحجز: قبل كل المواعيد بيومين
    private static readonly DateTimeOffset BookingTime = new(2026, 11, 1, 7, 0, 0, TimeSpan.Zero);

    // وقت الكشف الأول (بعد بدايته بساعة)
    private static readonly DateTimeOffset DuringFirstVisit = new(2026, 11, 3, 8, 0, 0, TimeSpan.Zero);

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

    private static async Task<(int DoctorId, int PatientId)> SeedAsync(
        AppDbContext db, decimal followUpPercent, int windowDays)
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
            FollowUpFeePercent = followUpPercent,
            FollowUpWindowDays = windowDays,
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

    // كشف أول مكتمل: حجز في العيادة، check-in، ثم complete
    private static async Task<Appointment> CompleteFirstVisitAsync(AppDbContext db, int doctorId, int patientId)
    {
        var booking = new AppointmentService(db, new FixedClock(BookingTime));
        var appointment = await booking.BookAsync(new BookAppointmentRequest(
            patientId, doctorId, FirstSlotUtc, AppointmentType.New, PaymentMethod.AtClinic));

        var atClinic = new AppointmentService(db, new FixedClock(DuringFirstVisit));
        await atClinic.CheckInAsync(appointment.Id);
        await atClinic.CompleteAsync(appointment.Id);

        return appointment;
    }

    [Fact]
    public async Task Follow_up_within_window_after_completed_visit_gets_discounted_fee()
    {
        using var connection = new SqliteConnection("DataSource=:memory:");
        connection.Open();
        using var db = CreateDb(connection);
        var (doctorId, patientId) = await SeedAsync(db, followUpPercent: 50m, windowDays: 14);

        var first = await CompleteFirstVisitAsync(db, doctorId, patientId);

        var booking = new AppointmentService(db, new FixedClock(BookingTime));
        var followUp = await booking.BookAsync(new BookAppointmentRequest(
            patientId, doctorId, FollowUpWithinUtc, AppointmentType.FollowUp, PaymentMethod.AtClinic));

        Assert.Equal(AppointmentType.FollowUp, followUp.Type);
        Assert.Equal(150m, followUp.PriceSnapshot);
        Assert.Equal(first.Id, followUp.PreviousAppointmentId);
    }

    [Fact]
    public async Task Follow_up_outside_window_becomes_normal_visit_at_full_fee()
    {
        using var connection = new SqliteConnection("DataSource=:memory:");
        connection.Open();
        using var db = CreateDb(connection);
        var (doctorId, patientId) = await SeedAsync(db, followUpPercent: 50m, windowDays: 14);

        await CompleteFirstVisitAsync(db, doctorId, patientId);

        var booking = new AppointmentService(db, new FixedClock(BookingTime));
        var followUp = await booking.BookAsync(new BookAppointmentRequest(
            patientId, doctorId, FollowUpOutsideUtc, AppointmentType.FollowUp, PaymentMethod.AtClinic));

        Assert.Equal(AppointmentType.New, followUp.Type);
        Assert.Equal(300m, followUp.PriceSnapshot);
        Assert.Null(followUp.PreviousAppointmentId);
    }

    [Fact]
    public async Task Follow_up_without_completed_visit_becomes_normal_visit_at_full_fee()
    {
        using var connection = new SqliteConnection("DataSource=:memory:");
        connection.Open();
        using var db = CreateDb(connection);
        var (doctorId, patientId) = await SeedAsync(db, followUpPercent: 50m, windowDays: 14);

        // كشف أول لسه Confirmed ومش مكتمل
        var booking = new AppointmentService(db, new FixedClock(BookingTime));
        await booking.BookAsync(new BookAppointmentRequest(
            patientId, doctorId, FirstSlotUtc, AppointmentType.New, PaymentMethod.AtClinic));

        var followUp = await booking.BookAsync(new BookAppointmentRequest(
            patientId, doctorId, FollowUpWithinUtc, AppointmentType.FollowUp, PaymentMethod.AtClinic));

        Assert.Equal(AppointmentType.New, followUp.Type);
        Assert.Equal(300m, followUp.PriceSnapshot);
        Assert.Null(followUp.PreviousAppointmentId);
    }

    [Fact]
    public async Task Follow_up_with_zero_percent_is_free()
    {
        using var connection = new SqliteConnection("DataSource=:memory:");
        connection.Open();
        using var db = CreateDb(connection);
        var (doctorId, patientId) = await SeedAsync(db, followUpPercent: 0m, windowDays: 14);

        var first = await CompleteFirstVisitAsync(db, doctorId, patientId);

        var booking = new AppointmentService(db, new FixedClock(BookingTime));
        var followUp = await booking.BookAsync(new BookAppointmentRequest(
            patientId, doctorId, FollowUpWithinUtc, AppointmentType.FollowUp, PaymentMethod.AtClinic));

        Assert.Equal(AppointmentType.FollowUp, followUp.Type);
        Assert.Equal(0m, followUp.PriceSnapshot);
        Assert.Equal(first.Id, followUp.PreviousAppointmentId);
    }
}
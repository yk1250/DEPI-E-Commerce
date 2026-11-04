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

public class AppointmentQueriesTests
{
    // وقت الحجز: قبل كل المواعيد بيومين
    private static readonly DateTimeOffset BookingTime = new(2026, 11, 1, 7, 0, 0, TimeSpan.Zero);

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

    private static async Task<int> AddPatientAsync(AppDbContext db, string name)
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

        var patient = new Patient { UserId = user.Id, DateOfBirth = new DateTime(1995, 1, 1) };
        db.Patients.Add(patient);
        await db.SaveChangesAsync();
        return patient.Id;
    }

    // بيرجّع الدكتور 1، وبيضيف مريضين
    private static async Task<(int DoctorId, int PatientA, int PatientB)> SeedAsync(AppDbContext db)
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
        db.Users.Add(doctorUser);
        await db.SaveChangesAsync();

        var doctor = new Doctor
        {
            UserId = doctorUser.Id,
            SpecialtyId = specialty.Id,
            ClinicId = clinic.Id,
            ConsultationFee = 300m,
            IsApproved = true
        };
        db.Doctors.Add(doctor);
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

        var patientA = await AddPatientAsync(db, "patientA");
        var patientB = await AddPatientAsync(db, "patientB");

        return (doctor.Id, patientA, patientB);
    }

    [Fact]
    public async Task Patient_sees_only_own_appointments_ordered_by_start()
    {
        using var connection = new SqliteConnection("DataSource=:memory:");
        connection.Open();
        using var db = CreateDb(connection);
        var (doctorId, patientA, patientB) = await SeedAsync(db);

        var booking = new AppointmentService(db, new FixedClock(BookingTime));

        // المريض A: يومين مختلفين
        await booking.BookAsync(new BookAppointmentRequest(
            patientA, doctorId, new DateTime(2026, 11, 10, 7, 0, 0, DateTimeKind.Utc),
            AppointmentType.New, PaymentMethod.AtClinic));
        await booking.BookAsync(new BookAppointmentRequest(
            patientA, doctorId, new DateTime(2026, 11, 3, 7, 0, 0, DateTimeKind.Utc),
            AppointmentType.New, PaymentMethod.AtClinic));

        // المريض B: موعد تالت، لازم ما يظهرش للمريض A
        await booking.BookAsync(new BookAppointmentRequest(
            patientB, doctorId, new DateTime(2026, 11, 3, 7, 30, 0, DateTimeKind.Utc),
            AppointmentType.New, PaymentMethod.AtClinic));

        var queries = new AppointmentQueries(db);

        var mine = await queries.GetPatientAppointmentsAsync(patientA);

        Assert.Equal(2, mine.Count);
        Assert.All(mine, a => Assert.Equal(patientA, a.PatientId));
        Assert.Equal(new DateTime(2026, 11, 3, 7, 0, 0, DateTimeKind.Utc), mine[0].StartAt);
        Assert.Equal(new DateTime(2026, 11, 10, 7, 0, 0, DateTimeKind.Utc), mine[1].StartAt);
    }

    [Fact]
    public async Task Doctor_sees_only_appointments_of_requested_day()
    {
        using var connection = new SqliteConnection("DataSource=:memory:");
        connection.Open();
        using var db = CreateDb(connection);
        var (doctorId, patientA, patientB) = await SeedAsync(db);

        var booking = new AppointmentService(db, new FixedClock(BookingTime));

        await booking.BookAsync(new BookAppointmentRequest(
            patientA, doctorId, new DateTime(2026, 11, 3, 7, 0, 0, DateTimeKind.Utc),
            AppointmentType.New, PaymentMethod.AtClinic));
        await booking.BookAsync(new BookAppointmentRequest(
            patientB, doctorId, new DateTime(2026, 11, 3, 7, 30, 0, DateTimeKind.Utc),
            AppointmentType.New, PaymentMethod.AtClinic));
        await booking.BookAsync(new BookAppointmentRequest(
            patientA, doctorId, new DateTime(2026, 11, 10, 7, 0, 0, DateTimeKind.Utc),
            AppointmentType.New, PaymentMethod.AtClinic));

        var queries = new AppointmentQueries(db);

        var day1 = await queries.GetDoctorAppointmentsAsync(doctorId, new DateOnly(2026, 11, 3));
        var day2 = await queries.GetDoctorAppointmentsAsync(doctorId, new DateOnly(2026, 11, 10));

        Assert.Equal(2, day1.Count);
        Assert.Single(day2);
        Assert.Equal(new DateTime(2026, 11, 10, 7, 0, 0, DateTimeKind.Utc), day2[0].StartAt);
    }

    [Fact]
    public async Task Doctor_day_uses_cairo_midnight_not_utc_midnight()
    {
        using var connection = new SqliteConnection("DataSource=:memory:");
        connection.Open();
        using var db = CreateDb(connection);
        var (doctorId, patientA, _) = await SeedAsync(db);

        var booking = new AppointmentService(db, new FixedClock(BookingTime));

        // 1:00 صباحاً يوم 3 نوفمبر بتوقيت القاهرة = 2 نوفمبر 23:00 UTC
        // المفروض يتحسب في يوم 3 نوفمبر (بتوقيت الدكتور)، مش 2 نوفمبر
        // ملاحظة: الدكتور بيشتغل الثلاثاء بس، فالحجز هيتقفل بـ validation
        // لو الساعة خارج الدوام. فنستخدم دوام يغطي 1 صباحاً في الاختبار
        db.WorkingSchedules.Add(new WorkingSchedule
        {
            DoctorId = doctorId,
            DayOfWeek = DayOfWeek.Tuesday,
            StartTime = new TimeOnly(0, 0),
            EndTime = new TimeOnly(2, 0),
            SlotDurationMinutes = 60,
            IsActive = true
        });
        await db.SaveChangesAsync();

        await booking.BookAsync(new BookAppointmentRequest(
            patientA, doctorId, new DateTime(2026, 11, 2, 23, 0, 0, DateTimeKind.Utc),
            AppointmentType.New, PaymentMethod.AtClinic));

        var queries = new AppointmentQueries(db);

        var nov3 = await queries.GetDoctorAppointmentsAsync(doctorId, new DateOnly(2026, 11, 3));
        var nov2 = await queries.GetDoctorAppointmentsAsync(doctorId, new DateOnly(2026, 11, 2));

        Assert.Single(nov3);
        Assert.Empty(nov2);
    }


}
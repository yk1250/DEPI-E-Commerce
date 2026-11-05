using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Clinic.Application.Common.Interfaces;
using Clinic.Application.Features.Appointments;
using Clinic.Application.Features.Waitlist;
using Clinic.Domain.Entities;
using Clinic.Domain.Enums;
using Clinic.Infrastructure.Identity;
using Clinic.Infrastructure.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Clinic.Application.Tests.Booking;

public class WaitlistLinkTests
{
    // الثلاثاء 17 نوفمبر 2026، 6:00 مساءً بتوقيت القاهرة = 16:00 UTC (داخل دوام 4-8)
    private static readonly DateTime SlotUtc = new(2026, 11, 17, 16, 0, 0, DateTimeKind.Utc);
    private static readonly DateOnly Day = new(2026, 11, 17);

    // وقت الحجز والإلغاء: قبل الموعد بأسبوع (عشان قاعدة الساعتين ما تمنعش)
    private static readonly DateTimeOffset BookingTime = new(2026, 11, 10, 7, 0, 0, TimeSpan.Zero);

    private sealed class FixedClock : TimeProvider
    {
        private readonly DateTimeOffset _now;

        public FixedClock(DateTimeOffset now)
        {
            _now = now;
        }

        public override DateTimeOffset GetUtcNow() => _now;
    }

    private sealed class FakeEmailSender : IEmailSender
    {
        public List<string> SentTo { get; } = new();

        public Task SendAsync(string to, string subject, string body, CancellationToken ct = default)
        {
            SentTo.Add(to);
            return Task.CompletedTask;
        }
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

    private static async Task<int> SeedDoctorAsync(AppDbContext db)
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

        // دوام الثلاثاء من 4 لـ 8 المساء، الكشف 30 دقيقة
        db.WorkingSchedules.Add(new WorkingSchedule
        {
            DoctorId = doctor.Id,
            DayOfWeek = DayOfWeek.Tuesday,
            StartTime = new TimeOnly(16, 0),
            EndTime = new TimeOnly(20, 0),
            SlotDurationMinutes = 30,
            IsActive = true
        });
        await db.SaveChangesAsync();

        return doctor.Id;
    }

    private static async Task<(int PatientId, string Email)> AddPatientAsync(AppDbContext db, string name)
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

        return (patient.Id, user.Email!);
    }

    [Fact]
    public async Task Patient_cancel_notifies_first_waiting_patient()
    {
        using var connection = new SqliteConnection("DataSource=:memory:");
        connection.Open();
        using var db = CreateDb(connection);
        var doctorId = await SeedDoctorAsync(db);
        var (patientA, _) = await AddPatientAsync(db, "patientA");
        var (patientB, emailB) = await AddPatientAsync(db, "patientB");

        var booking = new AppointmentService(db, new FixedClock(BookingTime));
        var appointment = await booking.BookAsync(new BookAppointmentRequest(
            patientA, doctorId, SlotUtc, AppointmentType.New, PaymentMethod.AtClinic));

        var sender = new FakeEmailSender();
        var waitlist = new WaitlistService(db, sender);
        await waitlist.JoinAsync(patientB, doctorId, Day);

        var cancelService = new AppointmentService(db, new FixedClock(BookingTime), waitlist);
        await cancelService.CancelByPatientAsync(appointment.Id, patientA, null);

        Assert.Single(sender.SentTo);
        Assert.Equal(emailB, sender.SentTo[0]);
    }

    [Fact]
    public async Task Doctor_cancel_notifies_first_waiting_patient()
    {
        using var connection = new SqliteConnection("DataSource=:memory:");
        connection.Open();
        using var db = CreateDb(connection);
        var doctorId = await SeedDoctorAsync(db);
        var (patientA, _) = await AddPatientAsync(db, "patientA");
        var (patientB, emailB) = await AddPatientAsync(db, "patientB");

        var booking = new AppointmentService(db, new FixedClock(BookingTime));
        var appointment = await booking.BookAsync(new BookAppointmentRequest(
            patientA, doctorId, SlotUtc, AppointmentType.New, PaymentMethod.AtClinic));

        var sender = new FakeEmailSender();
        var waitlist = new WaitlistService(db, sender);
        await waitlist.JoinAsync(patientB, doctorId, Day);

        var cancelService = new AppointmentService(db, new FixedClock(BookingTime), waitlist);
        await cancelService.CancelByDoctorAsync(appointment.Id, doctorId, "طارئ");

        Assert.Single(sender.SentTo);
        Assert.Equal(emailB, sender.SentTo[0]);
    }

    [Fact]
    public async Task Cancel_with_empty_waitlist_sends_nothing()
    {
        using var connection = new SqliteConnection("DataSource=:memory:");
        connection.Open();
        using var db = CreateDb(connection);
        var doctorId = await SeedDoctorAsync(db);
        var (patientA, _) = await AddPatientAsync(db, "patientA");

        var booking = new AppointmentService(db, new FixedClock(BookingTime));
        var appointment = await booking.BookAsync(new BookAppointmentRequest(
            patientA, doctorId, SlotUtc, AppointmentType.New, PaymentMethod.AtClinic));

        var sender = new FakeEmailSender();
        var cancelService = new AppointmentService(db, new FixedClock(BookingTime), new WaitlistService(db, sender));
        await cancelService.CancelByPatientAsync(appointment.Id, patientA, null);

        Assert.Empty(sender.SentTo);
    }
}
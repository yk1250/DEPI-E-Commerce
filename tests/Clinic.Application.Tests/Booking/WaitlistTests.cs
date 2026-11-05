using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Clinic.Application.Common.Exceptions;
using Clinic.Application.Common.Interfaces;
using Clinic.Application.Features.Waitlist;
using Clinic.Domain.Entities;
using Clinic.Infrastructure.Identity;
using Clinic.Infrastructure.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Clinic.Application.Tests.Booking;

public class WaitlistTests
{
    private static readonly DateOnly Day = new(2026, 11, 17);

    // بيسجّل الإيميلات بدل ما يبعتها فعلاً
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

    private static async Task<(int PatientId, Guid UserId, string Email)> AddPatientAsync(AppDbContext db, string name)
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

        return (patient.Id, user.Id, user.Email!);
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

        return doctor.Id;
    }

    [Fact]
    public async Task Patient_joins_waitlist_for_doctor_and_date()
    {
        using var connection = new SqliteConnection("DataSource=:memory:");
        connection.Open();
        using var db = CreateDb(connection);
        var doctorId = await SeedDoctorAsync(db);
        var patient = await AddPatientAsync(db, "patientA");

        var waitlist = new WaitlistService(db, new FakeEmailSender());

        var entry = await waitlist.JoinAsync(patient.PatientId, doctorId, Day);

        Assert.True(entry.IsActive);
        Assert.Null(entry.NotifiedAt);
        Assert.Equal(Day, entry.DesiredDate);
    }

    [Fact]
    public async Task Patient_cannot_join_same_doctor_and_date_twice()
    {
        using var connection = new SqliteConnection("DataSource=:memory:");
        connection.Open();
        using var db = CreateDb(connection);
        var doctorId = await SeedDoctorAsync(db);
        var patient = await AddPatientAsync(db, "patientA");

        var waitlist = new WaitlistService(db, new FakeEmailSender());
        await waitlist.JoinAsync(patient.PatientId, doctorId, Day);

        await Assert.ThrowsAsync<ConflictException>(() =>
            waitlist.JoinAsync(patient.PatientId, doctorId, Day));
    }

    [Fact]
    public async Task Opening_a_slot_notifies_only_the_first_waiting_patient()
    {
        using var connection = new SqliteConnection("DataSource=:memory:");
        connection.Open();
        using var db = CreateDb(connection);
        var doctorId = await SeedDoctorAsync(db);
        var patientA = await AddPatientAsync(db, "patientA");
        var patientB = await AddPatientAsync(db, "patientB");

        var sender = new FakeEmailSender();
        var waitlist = new WaitlistService(db, sender);

        var entryA = await waitlist.JoinAsync(patientA.PatientId, doctorId, Day);
        var entryB = await waitlist.JoinAsync(patientB.PatientId, doctorId, Day);

        var notified = await waitlist.NotifyFirstForOpenSlotAsync(doctorId, Day);

        Assert.Equal(1, notified);
        Assert.Single(sender.SentTo);
        Assert.Equal(patientA.Email, sender.SentTo[0]);

        var notifications = await db.Notifications.AsNoTracking().ToListAsync();
        Assert.Single(notifications);
        Assert.Equal(patientA.UserId, notifications[0].UserId);

        var rowA = await db.WaitlistEntries.AsNoTracking().FirstAsync(w => w.Id == entryA.Id);
        var rowB = await db.WaitlistEntries.AsNoTracking().FirstAsync(w => w.Id == entryB.Id);
        Assert.NotNull(rowA.NotifiedAt);
        Assert.Null(rowB.NotifiedAt);
    }

    [Fact]
    public async Task Opening_a_slot_with_empty_waitlist_notifies_nobody()
    {
        using var connection = new SqliteConnection("DataSource=:memory:");
        connection.Open();
        using var db = CreateDb(connection);
        var doctorId = await SeedDoctorAsync(db);

        var sender = new FakeEmailSender();
        var waitlist = new WaitlistService(db, sender);

        var notified = await waitlist.NotifyFirstForOpenSlotAsync(doctorId, Day);

        Assert.Equal(0, notified);
        Assert.Empty(sender.SentTo);
    }
}
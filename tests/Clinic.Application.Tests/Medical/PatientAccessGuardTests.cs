using Clinic.Application.Common.Exceptions;
using Clinic.Application.Common.Interfaces;
using Clinic.Application.Common.Security;
using Clinic.Application.Features.PatientAccess;
using Clinic.Domain.Entities;
using Clinic.Domain.Enums;
using Clinic.Infrastructure.Identity;
using Clinic.Infrastructure.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Clinic.Application.Tests.Medical;

public class PatientAccessGuardTests
{
    private sealed record World(int Doctor1, int Doctor2, int PatientA, int PatientB);

    private sealed class FakeCurrentUser : ICurrentUser
    {
        public bool IsAuthenticated { get; init; } = true;
        public Guid? UserId { get; init; } = Guid.NewGuid();
        public string? Role { get; init; }
        public int? PatientId { get; init; }
        public int? DoctorId { get; init; }
        public int? ClinicId { get; init; }
        public bool IsInRole(string role) => Role == role;
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

    // كل موعد بياخد وقت مختلف عشان الـ Unique Index على (DoctorId, StartAt)
    private int _slot;

    private static AppDbContext CreateDb(SqliteConnection connection)
    {
        var options = new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connection).Options;
        var db = new TestDbContext(options);
        db.Database.EnsureCreated();
        return db;
    }

    private static async Task<Guid> AddUserAsync(AppDbContext db, string name)
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
        var userId = await AddUserAsync(db, name);
        var patient = new Patient { UserId = userId, DateOfBirth = new DateTime(1995, 1, 1) };
        db.Patients.Add(patient);
        await db.SaveChangesAsync();
        return patient.Id;
    }

    private static async Task<int> AddDoctorAsync(AppDbContext db, string name, int specialtyId, int clinicId)
    {
        var userId = await AddUserAsync(db, name);
        var doctor = new Doctor
        {
            UserId = userId,
            SpecialtyId = specialtyId,
            ClinicId = clinicId,
            ConsultationFee = 300m,
            IsApproved = true
        };
        db.Doctors.Add(doctor);
        await db.SaveChangesAsync();
        return doctor.Id;
    }

    private static async Task<World> SeedAsync(AppDbContext db)
    {
        var specialty = new Specialty { NameAr = "باطنة", NameEn = "Internal" };
        var clinic = new MedicalClinic { Name = "Test Clinic", Address = "Test", City = "Fayoum" };
        db.Specialties.Add(specialty);
        db.Clinics.Add(clinic);
        await db.SaveChangesAsync();

        var doctor1 = await AddDoctorAsync(db, "doctor1", specialty.Id, clinic.Id);
        var doctor2 = await AddDoctorAsync(db, "doctor2", specialty.Id, clinic.Id);
        var patientA = await AddPatientAsync(db, "patientA");
        var patientB = await AddPatientAsync(db, "patientB");

        return new World(doctor1, doctor2, patientA, patientB);
    }

    private async Task AddAppointmentAsync(AppDbContext db, int doctorId, int patientId, AppointmentStatus status)
    {
        var start = new DateTime(2026, 11, 3, 7, 0, 0, DateTimeKind.Utc).AddMinutes(30 * _slot++);
        db.Appointments.Add(new Appointment
        {
            DoctorId = doctorId,
            PatientId = patientId,
            StartAt = start,
            EndAt = start.AddMinutes(30),
            Type = AppointmentType.New,
            Status = status,
            PriceSnapshot = 300m,
            CreatedAt = new DateTime(2026, 11, 1, 7, 0, 0, DateTimeKind.Utc)
        });
        await db.SaveChangesAsync();
    }

    private static PatientAccessGuard Guard(AppDbContext db, ICurrentUser user) => new(db, user);

    // ---------- المريض ----------

    [Fact]
    public async Task Patient_can_access_own_data()
    {
        using var connection = new SqliteConnection("DataSource=:memory:");
        connection.Open();
        using var db = CreateDb(connection);
        var w = await SeedAsync(db);

        var sut = Guard(db, new FakeCurrentUser { Role = Roles.Patient, PatientId = w.PatientA });

        Assert.True(await sut.CanAccessAsync(w.PatientA));
        await sut.EnsureAccessAsync(w.PatientA);
    }

    [Fact]
    public async Task Patient_cannot_access_another_patient()
    {
        using var connection = new SqliteConnection("DataSource=:memory:");
        connection.Open();
        using var db = CreateDb(connection);
        var w = await SeedAsync(db);

        var sut = Guard(db, new FakeCurrentUser { Role = Roles.Patient, PatientId = w.PatientA });

        Assert.False(await sut.CanAccessAsync(w.PatientB));
        await Assert.ThrowsAsync<ForbiddenException>(() => sut.EnsureAccessAsync(w.PatientB));
    }

    [Fact]
    public async Task Patient_without_patient_id_claim_is_denied()
    {
        using var connection = new SqliteConnection("DataSource=:memory:");
        connection.Open();
        using var db = CreateDb(connection);
        var w = await SeedAsync(db);

        var sut = Guard(db, new FakeCurrentUser { Role = Roles.Patient, PatientId = null });

        Assert.False(await sut.CanAccessAsync(w.PatientA));
    }

    // ---------- الدكتور ----------

    [Theory]
    [InlineData(AppointmentStatus.Confirmed)]
    [InlineData(AppointmentStatus.CheckedIn)]
    [InlineData(AppointmentStatus.Completed)]
    public async Task Doctor_can_access_patient_with_a_real_appointment(AppointmentStatus status)
    {
        using var connection = new SqliteConnection("DataSource=:memory:");
        connection.Open();
        using var db = CreateDb(connection);
        var w = await SeedAsync(db);
        await AddAppointmentAsync(db, w.Doctor1, w.PatientA, status);

        var sut = Guard(db, new FakeCurrentUser { Role = Roles.Doctor, DoctorId = w.Doctor1 });

        Assert.True(await sut.CanAccessAsync(w.PatientA));
        await sut.EnsureAccessAsync(w.PatientA);
    }

    [Theory]
    [InlineData(AppointmentStatus.PendingPayment)]
    [InlineData(AppointmentStatus.Cancelled)]
    [InlineData(AppointmentStatus.NoShow)]
    [InlineData(AppointmentStatus.Expired)]
    public async Task Doctor_cannot_access_patient_when_appointment_is_not_active(AppointmentStatus status)
    {
        using var connection = new SqliteConnection("DataSource=:memory:");
        connection.Open();
        using var db = CreateDb(connection);
        var w = await SeedAsync(db);
        await AddAppointmentAsync(db, w.Doctor1, w.PatientA, status);

        var sut = Guard(db, new FakeCurrentUser { Role = Roles.Doctor, DoctorId = w.Doctor1 });

        Assert.False(await sut.CanAccessAsync(w.PatientA));
        await Assert.ThrowsAsync<ForbiddenException>(() => sut.EnsureAccessAsync(w.PatientA));
    }

    [Fact]
    public async Task Doctor_cannot_access_patient_without_any_appointment()
    {
        using var connection = new SqliteConnection("DataSource=:memory:");
        connection.Open();
        using var db = CreateDb(connection);
        var w = await SeedAsync(db);

        var sut = Guard(db, new FakeCurrentUser { Role = Roles.Doctor, DoctorId = w.Doctor1 });

        Assert.False(await sut.CanAccessAsync(w.PatientA));
    }

    [Fact]
    public async Task Doctor_cannot_access_patient_who_booked_with_another_doctor()
    {
        using var connection = new SqliteConnection("DataSource=:memory:");
        connection.Open();
        using var db = CreateDb(connection);
        var w = await SeedAsync(db);
        await AddAppointmentAsync(db, w.Doctor2, w.PatientA, AppointmentStatus.Completed);

        var sut = Guard(db, new FakeCurrentUser { Role = Roles.Doctor, DoctorId = w.Doctor1 });

        Assert.False(await sut.CanAccessAsync(w.PatientA));
    }

    [Fact]
    public async Task Doctor_access_does_not_extend_to_other_patients()
    {
        using var connection = new SqliteConnection("DataSource=:memory:");
        connection.Open();
        using var db = CreateDb(connection);
        var w = await SeedAsync(db);
        await AddAppointmentAsync(db, w.Doctor1, w.PatientA, AppointmentStatus.Confirmed);

        var sut = Guard(db, new FakeCurrentUser { Role = Roles.Doctor, DoctorId = w.Doctor1 });

        Assert.True(await sut.CanAccessAsync(w.PatientA));
        Assert.False(await sut.CanAccessAsync(w.PatientB));
    }

    [Fact]
    public async Task Doctor_without_doctor_id_claim_is_denied()
    {
        using var connection = new SqliteConnection("DataSource=:memory:");
        connection.Open();
        using var db = CreateDb(connection);
        var w = await SeedAsync(db);
        await AddAppointmentAsync(db, w.Doctor1, w.PatientA, AppointmentStatus.Confirmed);

        var sut = Guard(db, new FakeCurrentUser { Role = Roles.Doctor, DoctorId = null });

        Assert.False(await sut.CanAccessAsync(w.PatientA));
    }

    // ---------- الأدمن والريسبشن وغير المسجّل ----------

    [Theory]
    [InlineData(Roles.Admin)]
    [InlineData(Roles.Receptionist)]
    public async Task Admin_and_receptionist_are_always_denied(string role)
    {
        using var connection = new SqliteConnection("DataSource=:memory:");
        connection.Open();
        using var db = CreateDb(connection);
        var w = await SeedAsync(db);
        await AddAppointmentAsync(db, w.Doctor1, w.PatientA, AppointmentStatus.Confirmed);

        // حتى لو معاهم ids ومفيش أي فرق، الدور نفسه ممنوع
        var sut = Guard(db, new FakeCurrentUser
        {
            Role = role,
            PatientId = w.PatientA,
            DoctorId = w.Doctor1,
            ClinicId = 1
        });

        Assert.False(await sut.CanAccessAsync(w.PatientA));
        await Assert.ThrowsAsync<ForbiddenException>(() => sut.EnsureAccessAsync(w.PatientA));
    }

    [Fact]
    public async Task Unknown_role_is_denied()
    {
        using var connection = new SqliteConnection("DataSource=:memory:");
        connection.Open();
        using var db = CreateDb(connection);
        var w = await SeedAsync(db);

        var sut = Guard(db, new FakeCurrentUser { Role = "Hacker", PatientId = w.PatientA });

        Assert.False(await sut.CanAccessAsync(w.PatientA));
    }

    [Fact]
    public async Task Unauthenticated_user_gets_401_not_403()
    {
        using var connection = new SqliteConnection("DataSource=:memory:");
        connection.Open();
        using var db = CreateDb(connection);
        var w = await SeedAsync(db);

        var sut = Guard(db, new FakeCurrentUser
        {
            IsAuthenticated = false,
            Role = Roles.Patient,
            PatientId = w.PatientA
        });

        Assert.False(await sut.CanAccessAsync(w.PatientA));
        await Assert.ThrowsAsync<UnauthorizedException>(() => sut.EnsureAccessAsync(w.PatientA));
    }
}

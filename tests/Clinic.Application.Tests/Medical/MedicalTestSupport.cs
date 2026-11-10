using Clinic.Application.Common.Interfaces;
using Clinic.Domain.Entities;
using Clinic.Domain.Enums;
using Clinic.Infrastructure.Identity;
using Clinic.Infrastructure.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Clinic.Application.Tests.Medical;

/// مستخدم حالي للاختبارات.
internal sealed class TestCurrentUser : ICurrentUser
{
    public bool IsAuthenticated { get; init; } = true;
    public Guid? UserId { get; init; } = Guid.NewGuid();
    public string? Role { get; init; }
    public int? PatientId { get; init; }
    public int? DoctorId { get; init; }
    public int? ClinicId { get; init; }
    public bool IsInRole(string role) => Role == role;
}

/// ساعة ثابتة للاختبارات.
internal sealed class TestClock : TimeProvider
{
    private readonly DateTimeOffset _now;

    public TestClock(DateTimeOffset now)
    {
        _now = now;
    }

    public override DateTimeOffset GetUtcNow() => _now;
}

/// داتابيز SQLite في الذاكرة فيها دكتور ومريضين.
/// (SQLite ما بيولّدش RowVersion زي SQL Server، فبنقفل التوليد للاختبار بس.)
internal sealed class MedicalTestEnv : IDisposable
{
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

    private readonly SqliteConnection _connection;
    private int _slot;

    public AppDbContext Db { get; }
    public int Doctor1 { get; private set; }
    public int PatientA { get; private set; }
    public Guid PatientAUserId { get; private set; }
    public int PatientB { get; private set; }
    public Guid PatientBUserId { get; private set; }

    private MedicalTestEnv(SqliteConnection connection, AppDbContext db)
    {
        _connection = connection;
        Db = db;
    }

    public static async Task<MedicalTestEnv> CreateAsync()
    {
        var connection = new SqliteConnection("DataSource=:memory:");
        connection.Open();

        var options = new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connection).Options;
        var db = new TestDbContext(options);
        db.Database.EnsureCreated();

        var env = new MedicalTestEnv(connection, db);

        var specialty = new Specialty { NameAr = "باطنة", NameEn = "Internal" };
        var clinic = new MedicalClinic { Name = "Test Clinic", Address = "Test", City = "Fayoum" };
        db.Specialties.Add(specialty);
        db.Clinics.Add(clinic);
        await db.SaveChangesAsync();

        var doctorUserId = await AddUserAsync(db, "doctor1");
        var doctor = new Doctor
        {
            UserId = doctorUserId,
            SpecialtyId = specialty.Id,
            ClinicId = clinic.Id,
            ConsultationFee = 300m,
            IsApproved = true
        };
        db.Doctors.Add(doctor);
        await db.SaveChangesAsync();
        env.Doctor1 = doctor.Id;

        (env.PatientA, env.PatientAUserId) = await AddPatientAsync(db, "patientA");
        (env.PatientB, env.PatientBUserId) = await AddPatientAsync(db, "patientB");

        return env;
    }

    /// بيضيف موعد وبيرجّع الـ Id بتاعه.
    public async Task<int> AddAppointmentAsync(int doctorId, int patientId, AppointmentStatus status)
    {
        var start = new DateTime(2026, 11, 3, 7, 0, 0, DateTimeKind.Utc).AddMinutes(30 * _slot++);
        var appointment = new Appointment
        {
            DoctorId = doctorId,
            PatientId = patientId,
            StartAt = start,
            EndAt = start.AddMinutes(30),
            Type = AppointmentType.New,
            Status = status,
            PriceSnapshot = 300m,
            CreatedAt = new DateTime(2026, 11, 1, 7, 0, 0, DateTimeKind.Utc)
        };
        Db.Appointments.Add(appointment);
        await Db.SaveChangesAsync();
        return appointment.Id;
    }

    public void Dispose()
    {
        Db.Dispose();
        _connection.Dispose();
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

    private static async Task<(int PatientId, Guid UserId)> AddPatientAsync(AppDbContext db, string name)
    {
        var userId = await AddUserAsync(db, name);
        var patient = new Patient { UserId = userId, DateOfBirth = new DateTime(1995, 1, 1) };
        db.Patients.Add(patient);
        await db.SaveChangesAsync();
        return (patient.Id, userId);
    }
}

using Clinic.Application.Common.Exceptions;
using Clinic.Application.Common.Interfaces;
using Clinic.Application.Common.Security;
using Clinic.Application.Features.Medical;
using Clinic.Application.Features.PatientAccess;
using Clinic.Domain.Entities;
using Clinic.Domain.Enums;
using Clinic.Infrastructure.Identity;
using Clinic.Infrastructure.Persistence;
using Clinic.Infrastructure.Security.Encryption;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Clinic.Application.Tests.Medical;

public class MedicalHistoryServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 7, 10, 0, 0, TimeSpan.Zero);

    private sealed class FixedClock : TimeProvider
    {
        private readonly DateTimeOffset _now;

        public FixedClock(DateTimeOffset now)
        {
            _now = now;
        }

        public override DateTimeOffset GetUtcNow() => _now;
    }

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

    /// داتابيز في الذاكرة فيها دكتور ومريضين.
    private sealed class Env : IDisposable
    {
        private readonly SqliteConnection _connection;
        private int _slot;

        public AppDbContext Db { get; }
        public int Doctor1 { get; private set; }
        public int PatientA { get; private set; }
        public int PatientB { get; private set; }

        private Env(SqliteConnection connection, AppDbContext db)
        {
            _connection = connection;
            Db = db;
        }

        public static async Task<Env> CreateAsync()
        {
            var connection = new SqliteConnection("DataSource=:memory:");
            connection.Open();

            var options = new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connection).Options;
            var db = new TestDbContext(options);
            db.Database.EnsureCreated();

            var env = new Env(connection, db);

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
            env.PatientA = await AddPatientAsync(db, "patientA");
            env.PatientB = await AddPatientAsync(db, "patientB");

            return env;
        }

        public async Task AddAppointmentAsync(int doctorId, int patientId, AppointmentStatus status)
        {
            var start = new DateTime(2026, 11, 3, 7, 0, 0, DateTimeKind.Utc).AddMinutes(30 * _slot++);
            Db.Appointments.Add(new Appointment
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
            await Db.SaveChangesAsync();
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

        private static async Task<int> AddPatientAsync(AppDbContext db, string name)
        {
            var userId = await AddUserAsync(db, name);
            var patient = new Patient { UserId = userId, DateOfBirth = new DateTime(1995, 1, 1) };
            db.Patients.Add(patient);
            await db.SaveChangesAsync();
            return patient.Id;
        }
    }

    private static MedicalHistoryService Service(AppDbContext db, ICurrentUser user)
    {
        var encryptor = new AesGcmFieldEncryptor(new EncryptionSettings(Enumerable.Repeat((byte)9, 32).ToArray()));
        var guard = new PatientAccessGuard(db, user);
        return new MedicalHistoryService(db, encryptor, guard, user, new FixedClock(Now));
    }

    private static FakeCurrentUser PatientUser(int patientId) =>
        new() { Role = Roles.Patient, PatientId = patientId };

    private static FakeCurrentUser DoctorUser(int doctorId) =>
        new() { Role = Roles.Doctor, DoctorId = doctorId };

    private static UpdateMedicalHistoryRequest Request(string? chronic, string? allergies, string? meds) =>
        new() { ChronicConditions = chronic, Allergies = allergies, CurrentMedications = meds };

    // ---------- قراءة وكتابة المريض لسجله ----------

    [Fact]
    public async Task Patient_without_history_gets_an_empty_result()
    {
        using var env = await Env.CreateAsync();
        var sut = Service(env.Db, PatientUser(env.PatientA));

        var result = await sut.GetMineAsync();

        Assert.Equal(env.PatientA, result.PatientId);
        Assert.Null(result.ChronicConditions);
        Assert.Null(result.Allergies);
        Assert.Null(result.CurrentMedications);
        Assert.Null(result.UpdatedAt);
    }

    [Fact]
    public async Task Update_then_get_returns_the_same_text()
    {
        using var env = await Env.CreateAsync();

        await Service(env.Db, PatientUser(env.PatientA))
            .UpdateMineAsync(Request("سكر وضغط", "Penicillin", "Insulin 10u"));

        var result = await Service(env.Db, PatientUser(env.PatientA)).GetMineAsync();

        Assert.Equal("سكر وضغط", result.ChronicConditions);
        Assert.Equal("Penicillin", result.Allergies);
        Assert.Equal("Insulin 10u", result.CurrentMedications);
    }

    [Fact]
    public async Task Values_are_stored_encrypted_in_the_database()
    {
        using var env = await Env.CreateAsync();

        await Service(env.Db, PatientUser(env.PatientA))
            .UpdateMineAsync(Request("Diabetes", "Penicillin", "Insulin"));

        var row = await env.Db.MedicalHistories.AsNoTracking().SingleAsync(h => h.PatientId == env.PatientA);

        Assert.NotNull(row.ChronicConditions);
        Assert.NotNull(row.Allergies);
        Assert.NotNull(row.CurrentMedications);
        Assert.DoesNotContain("Diabetes", row.ChronicConditions!);
        Assert.DoesNotContain("Penicillin", row.Allergies!);
        Assert.DoesNotContain("Insulin", row.CurrentMedications!);
    }

    [Fact]
    public async Task Second_update_replaces_the_first_and_keeps_a_single_row()
    {
        using var env = await Env.CreateAsync();
        var sut = Service(env.Db, PatientUser(env.PatientA));

        await sut.UpdateMineAsync(Request("old", "old", "old"));
        var result = await sut.UpdateMineAsync(Request("new", "newer", "newest"));

        Assert.Equal("new", result.ChronicConditions);
        Assert.Equal("newer", result.Allergies);
        Assert.Equal("newest", result.CurrentMedications);
        Assert.Equal(1, await env.Db.MedicalHistories.CountAsync(h => h.PatientId == env.PatientA));
    }

    [Fact]
    public async Task Update_sets_UpdatedAt_from_the_clock()
    {
        using var env = await Env.CreateAsync();

        var result = await Service(env.Db, PatientUser(env.PatientA))
            .UpdateMineAsync(Request("a", null, null));

        Assert.Equal(Now.UtcDateTime, result.UpdatedAt!.Value);
        Assert.Equal(DateTimeKind.Utc, result.UpdatedAt!.Value.Kind);
    }

    [Fact]
    public async Task Blank_values_are_stored_as_null()
    {
        using var env = await Env.CreateAsync();

        var result = await Service(env.Db, PatientUser(env.PatientA))
            .UpdateMineAsync(Request("   ", "", "  Aspirin  "));

        Assert.Null(result.ChronicConditions);
        Assert.Null(result.Allergies);
        Assert.Equal("Aspirin", result.CurrentMedications);

        var row = await env.Db.MedicalHistories.AsNoTracking().SingleAsync(h => h.PatientId == env.PatientA);
        Assert.Null(row.ChronicConditions);
        Assert.Null(row.Allergies);
    }

    [Fact]
    public async Task Update_with_all_nulls_clears_the_history()
    {
        using var env = await Env.CreateAsync();
        var sut = Service(env.Db, PatientUser(env.PatientA));
        await sut.UpdateMineAsync(Request("a", "b", "c"));

        var result = await sut.UpdateMineAsync(Request(null, null, null));

        Assert.Null(result.ChronicConditions);
        Assert.Null(result.Allergies);
        Assert.Null(result.CurrentMedications);
    }

    [Fact]
    public async Task One_patient_never_sees_another_patients_history()
    {
        using var env = await Env.CreateAsync();
        await Service(env.Db, PatientUser(env.PatientA)).UpdateMineAsync(Request("secret", "secret", "secret"));

        var result = await Service(env.Db, PatientUser(env.PatientB)).GetMineAsync();

        Assert.Equal(env.PatientB, result.PatientId);
        Assert.Null(result.ChronicConditions);
        Assert.Null(result.Allergies);
        Assert.Null(result.CurrentMedications);
    }

    // ---------- الدكتور ----------

    [Fact]
    public async Task Doctor_with_an_appointment_reads_the_decrypted_history()
    {
        using var env = await Env.CreateAsync();
        await Service(env.Db, PatientUser(env.PatientA)).UpdateMineAsync(Request("ضغط", "Penicillin", null));
        await env.AddAppointmentAsync(env.Doctor1, env.PatientA, AppointmentStatus.Completed);

        var result = await Service(env.Db, DoctorUser(env.Doctor1)).GetForPatientAsync(env.PatientA);

        Assert.Equal("ضغط", result.ChronicConditions);
        Assert.Equal("Penicillin", result.Allergies);
        Assert.Null(result.CurrentMedications);
    }

    [Fact]
    public async Task Doctor_with_an_appointment_but_no_history_gets_an_empty_result()
    {
        using var env = await Env.CreateAsync();
        await env.AddAppointmentAsync(env.Doctor1, env.PatientA, AppointmentStatus.Confirmed);

        var result = await Service(env.Db, DoctorUser(env.Doctor1)).GetForPatientAsync(env.PatientA);

        Assert.Equal(env.PatientA, result.PatientId);
        Assert.Null(result.UpdatedAt);
    }

    [Fact]
    public async Task Doctor_without_an_appointment_is_forbidden()
    {
        using var env = await Env.CreateAsync();
        await Service(env.Db, PatientUser(env.PatientA)).UpdateMineAsync(Request("secret", null, null));

        var sut = Service(env.Db, DoctorUser(env.Doctor1));

        await Assert.ThrowsAsync<ForbiddenException>(() => sut.GetForPatientAsync(env.PatientA));
    }

    [Fact]
    public async Task Doctor_cannot_write_a_medical_history()
    {
        using var env = await Env.CreateAsync();
        await env.AddAppointmentAsync(env.Doctor1, env.PatientA, AppointmentStatus.Confirmed);

        // حتى لو الـ token فيه PatientId بالغلط، الدور Doctor ممنوع من الكتابة
        var user = new FakeCurrentUser { Role = Roles.Doctor, DoctorId = env.Doctor1, PatientId = env.PatientA };
        var sut = Service(env.Db, user);

        await Assert.ThrowsAsync<ForbiddenException>(() => sut.UpdateMineAsync(Request("hack", null, null)));
        Assert.Equal(0, await env.Db.MedicalHistories.CountAsync());
    }

    // ---------- ممنوعين ----------

    [Fact]
    public async Task Patient_cannot_read_another_patient_through_the_doctor_path()
    {
        using var env = await Env.CreateAsync();
        var sut = Service(env.Db, PatientUser(env.PatientA));

        await Assert.ThrowsAsync<ForbiddenException>(() => sut.GetForPatientAsync(env.PatientB));
    }

    [Fact]
    public async Task Admin_cannot_read_a_patient_history()
    {
        using var env = await Env.CreateAsync();
        await env.AddAppointmentAsync(env.Doctor1, env.PatientA, AppointmentStatus.Confirmed);
        var sut = Service(env.Db, new FakeCurrentUser { Role = Roles.Admin });

        await Assert.ThrowsAsync<ForbiddenException>(() => sut.GetForPatientAsync(env.PatientA));
    }

    [Theory]
    [InlineData(Roles.Admin)]
    [InlineData(Roles.Receptionist)]
    public async Task Admin_and_receptionist_cannot_use_the_me_endpoints(string role)
    {
        using var env = await Env.CreateAsync();
        var sut = Service(env.Db, new FakeCurrentUser { Role = role, PatientId = env.PatientA });

        await Assert.ThrowsAsync<ForbiddenException>(() => sut.GetMineAsync());
        await Assert.ThrowsAsync<ForbiddenException>(() => sut.UpdateMineAsync(Request("x", null, null)));
    }

    [Fact]
    public async Task Unauthenticated_calls_get_401()
    {
        using var env = await Env.CreateAsync();
        var sut = Service(env.Db, new FakeCurrentUser { IsAuthenticated = false });

        await Assert.ThrowsAsync<UnauthorizedException>(() => sut.GetMineAsync());
        await Assert.ThrowsAsync<UnauthorizedException>(() => sut.GetForPatientAsync(env.PatientA));
    }
}

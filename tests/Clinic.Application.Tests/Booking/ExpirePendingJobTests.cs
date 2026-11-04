using Clinic.Application.Common.Interfaces;
using Clinic.Application.Features.Appointments;
using Clinic.Domain.Entities;
using Clinic.Domain.Enums;
using Clinic.Infrastructure.Booking;
using Clinic.Infrastructure.Identity;
using Clinic.Infrastructure.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Threading.Tasks;
using Xunit;

namespace Clinic.Application.Tests.Booking;

public class ExpirePendingJobTests
{
    private static readonly DateTime SlotUtc = new(2026, 11, 3, 7, 0, 0, DateTimeKind.Utc);

    private sealed class FixedClock : TimeProvider
    {
        private readonly DateTimeOffset _now;

        public FixedClock(DateTimeOffset now)
        {
            _now = now;
        }

        public override DateTimeOffset GetUtcNow() => _now;
    }

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

    [Fact]
    public async Task Run_once_expires_pending_appointment_whose_window_has_passed()
    {
        using var connection = new SqliteConnection("DataSource=:memory:");
        connection.Open();

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(connection)
            .Options;

        using (var setup = new TestDbContext(options))
        {
            setup.Database.EnsureCreated();

            var specialty = new Specialty { NameAr = "باطنة", NameEn = "Internal" };
            var clinic = new MedicalClinic { Name = "C", Address = "A", City = "Fayoum" };
            setup.Specialties.Add(specialty);
            setup.Clinics.Add(clinic);
            await setup.SaveChangesAsync();

            var doctorUser = new ApplicationUser { Id = Guid.NewGuid(), UserName = "d", Email = "d@test.local", FullName = "d" };
            var patientUser = new ApplicationUser { Id = Guid.NewGuid(), UserName = "p", Email = "p@test.local", FullName = "p" };
            setup.Users.Add(doctorUser);
            setup.Users.Add(patientUser);
            await setup.SaveChangesAsync();

            var doctor = new Doctor
            {
                UserId = doctorUser.Id,
                SpecialtyId = specialty.Id,
                ClinicId = clinic.Id,
                ConsultationFee = 300m,
                IsApproved = true
            };
            var patient = new Patient { UserId = patientUser.Id, DateOfBirth = new DateTime(1995, 1, 1) };
            setup.Doctors.Add(doctor);
            setup.Patients.Add(patient);
            await setup.SaveChangesAsync();

            setup.WorkingSchedules.Add(new WorkingSchedule
            {
                DoctorId = doctor.Id,
                DayOfWeek = DayOfWeek.Tuesday,
                StartTime = new TimeOnly(9, 0),
                EndTime = new TimeOnly(12, 0),
                SlotDurationMinutes = 30,
                IsActive = true
            });
            await setup.SaveChangesAsync();

            // حجز أونلاين وقت 1 نوفمبر، وبعدين ExpiresAt بيتعدّى
            var bookingService = new AppointmentService(setup, new FixedClock(new DateTimeOffset(2026, 11, 1, 7, 0, 0, TimeSpan.Zero)));
            var appointment = await bookingService.BookAsync(new BookAppointmentRequest(
                patient.Id, doctor.Id, SlotUtc, AppointmentType.New, PaymentMethod.Online));

            // نخلي ExpiresAt في الماضي بالنسبة لوقت الـ Job
            var row = await setup.Appointments.FirstAsync(a => a.Id == appointment.Id);
            row.ExpiresAt = new DateTime(2026, 11, 1, 7, 10, 0, DateTimeKind.Utc);
            await setup.SaveChangesAsync();
        }

        // Scope factory بيدي scope جديد لكل دورة، زي الـ DI الحقيقي
        var services = new ServiceCollection();
        services.AddScoped<AppDbContext>(_ => new TestDbContext(options));
        services.AddScoped<IAppDbContext>(sp => sp.GetRequiredService<AppDbContext>());
        services.AddSingleton<TimeProvider>(new FixedClock(new DateTimeOffset(2026, 11, 1, 8, 0, 0, TimeSpan.Zero)));
        services.AddScoped<AppointmentService>();
        var provider = services.BuildServiceProvider();

        var job = new ExpirePendingJob(provider.GetRequiredService<IServiceScopeFactory>());

        await job.RunOnceAsync(default);

        using var verify = new TestDbContext(options);
        var status = await verify.Appointments.AsNoTracking()
            .Select(a => a.Status)
            .FirstAsync();

        Assert.Equal(AppointmentStatus.Expired, status);
    }
}
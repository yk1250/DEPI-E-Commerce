using System;
using System.Linq;
using System.Threading.Tasks;
using Clinic.Application.Common.Exceptions;
using Clinic.Application.Common.Time;
using Clinic.Application.Features.Appointments;
using Clinic.Domain.Enums;
using Clinic.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Clinic.Application.Tests.Booking;

public class AppointmentConcurrencyTests
{
    private static AppDbContext CreateDb(string connectionString)
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlServer(connectionString)
            .Options;

        return new AppDbContext(options);
    }

    [Fact]
    public async Task Two_patients_booking_same_slot_at_once_only_one_succeeds()
    {
        var connectionString = Environment.GetEnvironmentVariable("CLINIC_TEST_CONNECTION");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            // لو المتغير مش موجود، الاختبار يتعدّى بدل ما يفشل على أي جهاز تاني
            return;
        }

        using var setupDb = CreateDb(connectionString);

        // ناخد أول دوام يوم الثلاثاء، ونستخدم الدكتور صاحبه عشان الميعاد يكون جوه ساعات شغله
        var schedule = await setupDb.WorkingSchedules
            .AsNoTracking()
            .FirstAsync(w => w.DayOfWeek == DayOfWeek.Tuesday && w.IsActive);

        var doctor = await setupDb.Doctors.AsNoTracking().FirstAsync(d => d.Id == schedule.DoctorId);
        var patient = await setupDb.Patients.AsNoTracking().FirstAsync();

        // الميعاد = أول دقيقة في دوام الدكتور، بتوقيت القاهرة وبعدين UTC
        var day = new DateOnly(2026, 11, 17);
        var slotUtc = ClinicTime.ToUtc(day, schedule.StartTime);

        // ننضف أي حجز قديم على نفس الميعاد عشان الاختبار يتكرر
        var old = setupDb.Appointments.Where(a => a.DoctorId == doctor.Id && a.StartAt == slotUtc);
        setupDb.Appointments.RemoveRange(old);
        await setupDb.SaveChangesAsync();

        using var dbA = CreateDb(connectionString);
        using var dbB = CreateDb(connectionString);

        var serviceA = new AppointmentService(dbA);
        var serviceB = new AppointmentService(dbB);

        var request = new BookAppointmentRequest(
            patient.Id, doctor.Id, slotUtc, AppointmentType.New, PaymentMethod.Online);

        var results = await Task.WhenAll(
            TryBookAsync(serviceA, request),
            TryBookAsync(serviceB, request));

        var successes = results.Count(r => r.Succeeded);
        var conflicts = results.Count(r => r.Conflict);

        Assert.Equal(1, successes);
        Assert.Equal(1, conflicts);

        var stored = await setupDb.Appointments.CountAsync(a =>
            a.DoctorId == doctor.Id
            && a.StartAt == slotUtc
            && a.Status != AppointmentStatus.Cancelled
            && a.Status != AppointmentStatus.Expired);
        Assert.Equal(1, stored);
    }

    private static async Task<(bool Succeeded, bool Conflict)> TryBookAsync(
        AppointmentService service, BookAppointmentRequest request)
    {
        try
        {
            await service.BookAsync(request);
            return (true, false);
        }
        catch (ConflictException)
        {
            return (false, true);
        }
    }
}
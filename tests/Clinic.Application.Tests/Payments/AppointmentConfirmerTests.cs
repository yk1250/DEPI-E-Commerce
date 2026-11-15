using Clinic.Application.Common.Exceptions;
using Clinic.Application.Features.Payments;
using Clinic.Application.Tests.Medical;
using Clinic.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace Clinic.Application.Tests.Payments;

public class AppointmentConfirmerTests
{
    private static readonly DateTime Now = new(2026, 11, 7, 10, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task Pending_payment_becomes_confirmed_and_loses_its_expiry()
    {
        using var env = await MedicalTestEnv.CreateAsync();
        var id = await env.AddAppointmentAsync(env.Doctor1, env.PatientA, AppointmentStatus.PendingPayment, Now.AddMinutes(10));

        await new AppointmentConfirmer(env.Db).ConfirmAsync(id);

        var row = await env.Db.Appointments.AsNoTracking().SingleAsync(a => a.Id == id);
        Assert.Equal(AppointmentStatus.Confirmed, row.Status);
        Assert.Null(row.ExpiresAt);
    }

    [Theory]
    [InlineData(AppointmentStatus.Confirmed)]
    [InlineData(AppointmentStatus.CheckedIn)]
    [InlineData(AppointmentStatus.Completed)]
    public async Task Already_confirmed_appointments_are_left_alone(AppointmentStatus status)
    {
        using var env = await MedicalTestEnv.CreateAsync();
        var id = await env.AddAppointmentAsync(env.Doctor1, env.PatientA, status);

        await new AppointmentConfirmer(env.Db).ConfirmAsync(id);

        var row = await env.Db.Appointments.AsNoTracking().SingleAsync(a => a.Id == id);
        Assert.Equal(status, row.Status);
    }

    [Theory]
    [InlineData(AppointmentStatus.Expired)]
    [InlineData(AppointmentStatus.Cancelled)]
    [InlineData(AppointmentStatus.NoShow)]
    public async Task Expired_cancelled_or_missed_appointments_cannot_be_confirmed(AppointmentStatus status)
    {
        using var env = await MedicalTestEnv.CreateAsync();
        var id = await env.AddAppointmentAsync(env.Doctor1, env.PatientA, status);

        await Assert.ThrowsAsync<ConflictException>(() => new AppointmentConfirmer(env.Db).ConfirmAsync(id));

        var row = await env.Db.Appointments.AsNoTracking().SingleAsync(a => a.Id == id);
        Assert.Equal(status, row.Status);
    }

    [Fact]
    public async Task Missing_appointment_is_not_found()
    {
        using var env = await MedicalTestEnv.CreateAsync();

        await Assert.ThrowsAsync<NotFoundException>(() => new AppointmentConfirmer(env.Db).ConfirmAsync(9999));
    }
}

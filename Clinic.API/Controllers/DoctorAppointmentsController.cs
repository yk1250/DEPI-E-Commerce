using Clinic.Application.Common.Exceptions;
using Clinic.Application.Common.Interfaces;
using Clinic.Application.Common.Security;
using Clinic.Application.Features.Appointments;
using Clinic.Domain.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Clinic.API.Controllers;

[ApiController]
[Route("api/doctors/me/appointments")]
public class DoctorAppointmentsController : ControllerBase
{
    private readonly AppointmentQueries _queries;
    private readonly ICurrentUser _currentUser;

    public DoctorAppointmentsController(AppointmentQueries queries, ICurrentUser currentUser)
    {
        _queries = queries;
        _currentUser = currentUser;
    }

    /// مواعيد الدكتور الحالي في يوم معين (بتوقيت القاهرة). الـ DoctorId بيتاخد من الـ token بس.
    [HttpGet]
    [Authorize(Roles = Roles.Doctor)]
    [ProducesResponseType(typeof(IReadOnlyList<AppointmentDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> GetMine([FromQuery] DateOnly date, CancellationToken ct)
    {
        var doctorId = _currentUser.DoctorId
            ?? throw new ForbiddenException("Only doctors can view their appointments.");

        var appointments = await _queries.GetDoctorAppointmentsAsync(doctorId, date, ct);

        return Ok(appointments.Select(ToDto).ToList());
    }

    private static AppointmentDto ToDto(Appointment appointment) => new(
        appointment.Id,
        appointment.DoctorId,
        appointment.PatientId,
        DateTime.SpecifyKind(appointment.StartAt, DateTimeKind.Utc),
        DateTime.SpecifyKind(appointment.EndAt, DateTimeKind.Utc),
        appointment.Status,
        appointment.PriceSnapshot,
        appointment.ExpiresAt is null ? null : DateTime.SpecifyKind(appointment.ExpiresAt.Value, DateTimeKind.Utc));
}
using Clinic.Application.Common.Exceptions;
using Clinic.Application.Common.Interfaces;
using Clinic.Application.Common.Security;
using Clinic.Application.Features.Appointments;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Clinic.API.Controllers;

[ApiController]
[Route("api/appointments")]
public class AppointmentsController : ControllerBase
{
    private readonly AppointmentService _appointments;
    private readonly ICurrentUser _currentUser;

    public AppointmentsController(AppointmentService appointments, ICurrentUser currentUser)
    {
        _appointments = appointments;
        _currentUser = currentUser;
    }

    /// حجز موعد للمريض الحالي. الـ PatientId بيتاخد من الـ token بس.
    [HttpPost]
    [Authorize(Roles = Roles.Patient)]
    [ProducesResponseType(typeof(AppointmentDto), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> Book([FromBody] BookAppointmentBody body, CancellationToken ct)
    {
        var patientId = _currentUser.PatientId
            ?? throw new ForbiddenException("Only patients can book appointments.");

        // الوقت لازم يتبعت بـ UTC (مثلاً 2026-11-03T14:00:00Z) عشان ما يحصلش لخبطة في التوقيت
        if (body.StartAtUtc.Kind != DateTimeKind.Utc)
        {
            throw new AppValidationException("StartAtUtc must be sent in UTC (end with Z).");
        }

        var appointment = await _appointments.BookAsync(
            new BookAppointmentRequest(patientId, body.DoctorId, body.StartAtUtc, body.Type, body.PaymentMethod),
            ct);

        var dto = new AppointmentDto(
            appointment.Id,
            appointment.DoctorId,
            appointment.PatientId,
            appointment.StartAt,
            appointment.EndAt,
            appointment.Status,
            appointment.PriceSnapshot,
            appointment.ExpiresAt);

        return StatusCode(StatusCodes.Status201Created, dto);
    }
}
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
    private readonly AppointmentService _appointments;
    private readonly AppointmentQueries _queries;
    private readonly ICurrentUser _currentUser;

    public DoctorAppointmentsController(
        AppointmentService appointments,
        AppointmentQueries queries,
        ICurrentUser currentUser)
    {
        _appointments = appointments;
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

    /// الدكتور صاحب الموعد يلغي موعده. مفيش قاعدة ساعتين للدكتور. من PendingPayment أو Confirmed بس.
    [HttpPost("{id:int}/cancel")]
    [Authorize(Roles = Roles.Doctor)]
    [ProducesResponseType(typeof(AppointmentDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> CancelMine(
        int id,
        [FromBody] CancelAppointmentBody? body,
        CancellationToken ct)
    {
        var doctorId = _currentUser.DoctorId
            ?? throw new ForbiddenException("Only doctors can cancel their appointments.");

        var appointment = await _appointments.CancelByDoctorAsync(id, doctorId, body?.Reason, ct);

        return Ok(ToDto(appointment));
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
using Clinic.Application.Common.Exceptions;
using Clinic.Application.Common.Interfaces;
using Clinic.Application.Common.Security;
using Clinic.Application.Features.Appointments;
using Clinic.Domain.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Clinic.API.Controllers;

[ApiController]
[Route("api/appointments")]
public class AppointmentsController : ControllerBase
{
    private readonly AppointmentService _appointments;
    private readonly AppointmentQueries _queries;
    private readonly ICurrentUser _currentUser;

    public AppointmentsController(
        AppointmentService appointments,
        AppointmentQueries queries,
        ICurrentUser currentUser)
    {
        _appointments = appointments;
        _queries = queries;
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

        return StatusCode(StatusCodes.Status201Created, ToDto(appointment));
    }

    /// المريض يلغي موعده. ممنوع قبل الموعد بأقل من ساعتين.
    [HttpPost("{id:int}/cancel")]
    [Authorize(Roles = Roles.Patient)]
    [ProducesResponseType(typeof(AppointmentDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> Cancel(
        int id,
        [FromBody] CancelAppointmentBody? body,
        CancellationToken ct)
    {
        var patientId = _currentUser.PatientId
            ?? throw new ForbiddenException("Only patients can cancel their appointments.");

        var appointment = await _appointments.CancelByPatientAsync(id, patientId, body?.Reason, ct);

        return Ok(ToDto(appointment));
    }

    /// الدكتور صاحب الموعد أو الريسبشن في نفس العيادة: المريض وصل. من Confirmed بس.
    [HttpPost("{id:int}/check-in")]
    [Authorize(Roles = Roles.Doctor + "," + Roles.Receptionist)]
    [ProducesResponseType(typeof(AppointmentDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> CheckIn(int id, CancellationToken ct)
    {
        await EnsureStaffCanManageAsync(id, allowReceptionist: true, ct);

        var appointment = await _appointments.CheckInAsync(id, ct);

        return Ok(ToDto(appointment));
    }

    /// الدكتور صاحب الموعد: خلص الكشف. من CheckedIn بس.
    [HttpPost("{id:int}/complete")]
    [Authorize(Roles = Roles.Doctor)]
    [ProducesResponseType(typeof(AppointmentDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> Complete(int id, CancellationToken ct)
    {
        await EnsureStaffCanManageAsync(id, allowReceptionist: false, ct);

        var appointment = await _appointments.CompleteAsync(id, ct);

        return Ok(ToDto(appointment));
    }

    /// الدكتور صاحب الموعد: المريض ما جاش. من Confirmed بس، وبعد بداية الموعد.
    [HttpPost("{id:int}/no-show")]
    [Authorize(Roles = Roles.Doctor)]
    [ProducesResponseType(typeof(AppointmentDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> NoShow(int id, CancellationToken ct)
    {
        await EnsureStaffCanManageAsync(id, allowReceptionist: false, ct);

        var appointment = await _appointments.MarkNoShowAsync(id, ct);

        return Ok(ToDto(appointment));
    }

    /// الدكتور لازم يكون صاحب الموعد. الريسبشن (لو مسموح) لازم يكون في نفس عيادة الدكتور.
    private async Task EnsureStaffCanManageAsync(int appointmentId, bool allowReceptionist, CancellationToken ct)
    {
        var scope = await _appointments.GetClinicScopeAsync(appointmentId, ct);

        if (_currentUser.IsInRole(Roles.Doctor))
        {
            if (_currentUser.DoctorId != scope.DoctorId)
            {
                throw new ForbiddenException("You can only manage your own appointments.");
            }

            return;
        }

        if (allowReceptionist && _currentUser.IsInRole(Roles.Receptionist))
        {
            if (_currentUser.ClinicId != scope.ClinicId)
            {
                throw new ForbiddenException("You can only manage appointments in your clinic.");
            }

            return;
        }

        throw new ForbiddenException("You do not have permission to perform this action.");
    }

    /// بنحوّل الأوقات لـ UTC صريح، عشان الـ JSON يكتب Z في الآخر.
    /// EF Core على SQL Server بيرجّع DateTime بـ Kind = Unspecified.
    private static AppointmentDto ToDto(Appointment appointment) => new(
        appointment.Id,
        appointment.DoctorId,
        appointment.PatientId,
        DateTime.SpecifyKind(appointment.StartAt, DateTimeKind.Utc),
        DateTime.SpecifyKind(appointment.EndAt, DateTimeKind.Utc),
        appointment.Status,
        appointment.PriceSnapshot,
        appointment.ExpiresAt is null ? null : DateTime.SpecifyKind(appointment.ExpiresAt.Value, DateTimeKind.Utc));



    /// مواعيد المريض الحالي، من الأقرب للأبعد. الـ PatientId بيتاخد من الـ token بس.
    [HttpGet("me")]
    [Authorize(Roles = Roles.Patient)]
    [ProducesResponseType(typeof(IReadOnlyList<AppointmentDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> GetMine(CancellationToken ct)
    {
        var patientId = _currentUser.PatientId
            ?? throw new ForbiddenException("Only patients can view their appointments.");

        var appointments = await _queries.GetPatientAppointmentsAsync(patientId, ct);

        return Ok(appointments.Select(ToDto).ToList());
    }





}
using Clinic.Application.Common.Exceptions;
using Clinic.Application.Common.Interfaces;
using Clinic.Application.Common.Security;
using Clinic.Application.Features.Waitlist;
using Clinic.Domain.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Clinic.API.Controllers;

[ApiController]
[Route("api/waitlist")]
public class WaitlistController : ControllerBase
{
    private readonly WaitlistService _waitlist;
    private readonly ICurrentUser _currentUser;

    public WaitlistController(WaitlistService waitlist, ICurrentUser currentUser)
    {
        _waitlist = waitlist;
        _currentUser = currentUser;
    }

    /// المريض يسجّل نفسه في قايمة انتظار دكتور ويوم معين. الـ PatientId بيتاخد من الـ token بس.
    [HttpPost]
    [Authorize(Roles = Roles.Patient)]
    [ProducesResponseType(typeof(WaitlistEntryDto), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> Join([FromBody] JoinWaitlistBody body, CancellationToken ct)
    {
        var patientId = _currentUser.PatientId
            ?? throw new ForbiddenException("Only patients can join the waitlist.");

        var entry = await _waitlist.JoinAsync(patientId, body.DoctorId, body.DesiredDate, ct);

        var dto = new WaitlistEntryDto(entry.Id, entry.DoctorId, entry.DesiredDate, entry.IsActive);

        return StatusCode(StatusCodes.Status201Created, dto);
    }
}
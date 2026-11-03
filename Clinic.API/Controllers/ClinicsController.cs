using Clinic.Application.Common.Exceptions;
using Clinic.Application.Common.Security;
using Clinic.Application.Features.Clinics;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Clinic.API.Controllers;

[ApiController]
[Route("api/clinics")]
public class ClinicsController : ControllerBase
{
    private readonly IClinicService _clinics;

    public ClinicsController(IClinicService clinics)
    {
        _clinics = clinics;
    }

    /// العيادات الشغالة (عام). includeInactive=true بيشتغل للأدمن بس.
    [HttpGet]
    [AllowAnonymous]
    [ProducesResponseType(typeof(IReadOnlyList<ClinicDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> List([FromQuery] bool includeInactive = false, CancellationToken ct = default)
    {
        var showAll = includeInactive && User.IsInRole(Roles.Admin);
        return Ok(await _clinics.ListAsync(showAll, ct));
    }

    [HttpGet("{id:int}", Name = "GetClinic")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(ClinicDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Get(int id, CancellationToken ct)
    {
        var clinic = await _clinics.GetAsync(id, ct);

        // العيادة الموقوفة مخفية عن غير الأدمن
        if (!clinic.IsActive && !User.IsInRole(Roles.Admin))
        {
            throw new NotFoundException("Clinic", id);
        }

        return Ok(clinic);
    }

    [HttpPost]
    [Authorize(Roles = Roles.Admin)]
    [ProducesResponseType(typeof(ClinicDto), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Create([FromBody] SaveClinicRequest request, CancellationToken ct)
    {
        var created = await _clinics.CreateAsync(request, ct);
        return CreatedAtRoute("GetClinic", new { id = created.Id }, created);
    }

    [HttpPut("{id:int}")]
    [Authorize(Roles = Roles.Admin)]
    [ProducesResponseType(typeof(ClinicDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Update(int id, [FromBody] SaveClinicRequest request, CancellationToken ct)
        => Ok(await _clinics.UpdateAsync(id, request, ct));

    /// بيرجّع 409 لو في دكاترة أو ريسبشن على العيادة. وقتها استخدم PUT واجعل IsActive = false.
    [HttpDelete("{id:int}")]
    [Authorize(Roles = Roles.Admin)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Delete(int id, CancellationToken ct)
    {
        await _clinics.DeleteAsync(id, ct);
        return NoContent();
    }
}

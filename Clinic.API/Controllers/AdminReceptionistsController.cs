using Clinic.Application.Common.Security;
using Clinic.Application.Features.Receptionists;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Clinic.API.Controllers;

/// إدارة الريسبشن (أدمن بس).
[ApiController]
[Route("api/admin/receptionists")]
[Authorize(Roles = Roles.Admin)]
[ProducesResponseType(StatusCodes.Status401Unauthorized)]
[ProducesResponseType(StatusCodes.Status403Forbidden)]
public class AdminReceptionistsController : ControllerBase
{
    private readonly IReceptionistService _receptionists;

    public AdminReceptionistsController(IReceptionistService receptionists)
    {
        _receptionists = receptionists;
    }

    [HttpPost]
    [ProducesResponseType(typeof(ReceptionistDto), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Create([FromBody] CreateReceptionistRequest request, CancellationToken ct)
    {
        var created = await _receptionists.CreateAsync(request, ct);
        return CreatedAtRoute("GetReceptionist", new { id = created.Id }, created);
    }

    [HttpGet]
    [ProducesResponseType(typeof(IReadOnlyList<ReceptionistDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> List([FromQuery] int? clinicId, CancellationToken ct)
        => Ok(await _receptionists.ListAsync(clinicId, ct));

    [HttpGet("{id:int}", Name = "GetReceptionist")]
    [ProducesResponseType(typeof(ReceptionistDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Get(int id, CancellationToken ct)
        => Ok(await _receptionists.GetAsync(id, ct));

    /// بيمسح الريسبشن وحسابه.
    [HttpDelete("{id:int}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Delete(int id, CancellationToken ct)
    {
        await _receptionists.DeleteAsync(id, ct);
        return NoContent();
    }
}

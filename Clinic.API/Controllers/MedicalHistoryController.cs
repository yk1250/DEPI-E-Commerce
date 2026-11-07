using Clinic.Application.Common.Security;
using Clinic.Application.Features.Medical;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Clinic.API.Controllers;

[ApiController]
[Route("api")]
public class MedicalHistoryController : ControllerBase
{
    private readonly MedicalHistoryService _service;

    public MedicalHistoryController(MedicalHistoryService service)
    {
        _service = service;
    }

    /// سجلي الطبي. الـ PatientId بيتاخد من الـ token بس.
    [HttpGet("medical-history/me")]
    [Authorize(Roles = Roles.Patient)]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    [ProducesResponseType(typeof(MedicalHistoryDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> GetMine(CancellationToken ct)
    {
        return Ok(await _service.GetMineAsync(ct));
    }

    /// أكتب سجلي الطبي (بيستبدل السجل كله، وأي حقل فاضي بيتمسح). الحقول بتتشفر قبل الحفظ.
    [HttpPut("medical-history/me")]
    [Authorize(Roles = Roles.Patient)]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    [ProducesResponseType(typeof(MedicalHistoryDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> UpdateMine([FromBody] UpdateMedicalHistoryRequest request, CancellationToken ct)
    {
        return Ok(await _service.UpdateMineAsync(request, ct));
    }

    /// الدكتور يشوف السجل الطبي لمريض عنده معاه موعد (Confirmed أو CheckedIn أو Completed) بس.
    [HttpGet("patients/{id:int}/medical-history")]
    [Authorize(Roles = Roles.Doctor)]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    [ProducesResponseType(typeof(MedicalHistoryDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> GetForPatient(int id, CancellationToken ct)
    {
        return Ok(await _service.GetForPatientAsync(id, ct));
    }
}

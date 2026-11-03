using Clinic.Application.Features.Slots;
using Microsoft.AspNetCore.Mvc;
namespace Clinic.API.Controllers;

[ApiController]
[Route("api/doctors/{doctorId:int}/slots")]
public class SlotsController : ControllerBase
{
    private readonly SlotService _slots;

    public SlotsController(SlotService slots)
    {
        _slots = slots;
    }

    /// المواعيد المتاحة لدكتور في يوم معين. الأوقات بتوقيت القاهرة.
    [HttpGet]
    [ProducesResponseType(typeof(IReadOnlyList<TimeOnly>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Get(int doctorId, [FromQuery] DateOnly date, CancellationToken ct)
    {
        var slots = await _slots.GetSlotsAsync(doctorId, date, ct);
        return Ok(slots);
    }
}
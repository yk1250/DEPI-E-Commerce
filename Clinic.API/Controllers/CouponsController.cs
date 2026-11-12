using Clinic.Application.Common.Security;
using Clinic.Application.Features.Coupons;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Clinic.API.Controllers;

[ApiController]
[Route("api/coupons")]
public class CouponsController : ControllerBase
{
    private readonly CouponService _service;

    public CouponsController(CouponService service)
    {
        _service = service;
    }

    /// معاينة خصم الكوبون على موعد بتاعك. مش بتستهلك الكوبون، الاستهلاك بيحصل وقت الدفع.
    [HttpPost("validate")]
    [Authorize(Roles = Roles.Patient)]
    [ProducesResponseType(typeof(CouponValidationDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Validate([FromBody] ValidateCouponRequest request, CancellationToken ct)
    {
        return Ok(await _service.ValidateAsync(request, ct));
    }
}

using Clinic.Application.Common.Security;
using Clinic.Application.Features.Payments;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Clinic.API.Controllers;

[ApiController]
[Route("api/payments")]
public class PaymentsController : ControllerBase
{
    private readonly ClinicPaymentService _clinicPayments;

    public PaymentsController(ClinicPaymentService clinicPayments)
    {
        _clinicPayments = clinicPayments;
    }

    /// الريسبشن يأكد إن المريض دفع في العيادة (موعد Confirmed أو CheckedIn في عيادته).
    /// الكوبون اختياري: بيتحسب الخصم ويتستهلك مع حفظ الدفع.
    [HttpPost("clinic/{appointmentId:int}/confirm")]
    [Authorize(Roles = Roles.Receptionist)]
    [ProducesResponseType(typeof(PaymentDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> ConfirmAtClinic(
        int appointmentId,
        [FromBody] ConfirmClinicPaymentRequest? request,
        CancellationToken ct)
    {
        return Ok(await _clinicPayments.ConfirmAsync(appointmentId, request?.CouponCode, ct));
    }
}

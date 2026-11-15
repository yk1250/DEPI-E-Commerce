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
    private readonly OnlinePaymentService _onlinePayments;

    public PaymentsController(ClinicPaymentService clinicPayments, OnlinePaymentService onlinePayments)
    {
        _clinicPayments = clinicPayments;
        _onlinePayments = onlinePayments;
    }

    /// المريض يبدأ دفع أونلاين لموعد مستني الدفع، ويرجّع رابط صفحة دفع Paymob.
    /// الكوبون اختياري: بيتحسب الخصم هنا وبيتستهلك بعد نجاح الدفع.
    [HttpPost("online")]
    [Authorize(Roles = Roles.Patient)]
    [ProducesResponseType(typeof(OnlinePaymentDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> InitiateOnline([FromBody] InitiateOnlinePaymentRequest request, CancellationToken ct)
    {
        return Ok(await _onlinePayments.InitiateAsync(request.AppointmentId, request.CouponCode, ct));
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

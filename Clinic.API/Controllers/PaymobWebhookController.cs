using System.Text.Json;
using Clinic.Application.Features.Payments;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Clinic.API.Controllers;

[ApiController]
[Route("api/payments/webhook")]
public class PaymobWebhookController : ControllerBase
{
    private readonly PaymobWebhookService _service;

    public PaymobWebhookController(PaymobWebhookService service)
    {
        _service = service;
    }

    /// إشعار "Transaction Processed" من Paymob. من غير تسجيل دخول، والأمان بالتحقق من توقيع HMAC (?hmac=).
    /// 401 لو التوقيع غلط. 200 لأي إشعار سليم (حتى لو مفيش حاجة تتعمل). وأي خطأ مؤقت بيرجّع 500 وPaymob بيعيد الإرسال.
    [HttpPost("paymob")]
    [AllowAnonymous]
    [RequestSizeLimit(65536)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> Receive([FromBody] JsonElement body, [FromQuery] string? hmac, CancellationToken ct)
    {
        var result = await _service.ProcessAsync(body, hmac, ct);
        return result == PaymobWebhookResult.InvalidSignature ? Unauthorized() : Ok();
    }
}

using Clinic.Application.Common.Security;
using Clinic.Application.Features.Attachments;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Clinic.API.Controllers;

[ApiController]
[Route("api")]
public class AttachmentsController : ControllerBase
{
    private readonly AttachmentService _service;

    public AttachmentsController(AttachmentService service)
    {
        _service = service;
    }

    /// المريض يرفع ملف (PDF أو JPG أو PNG، حد أقصى 10 ميجا) لموعد بتاعه.
    [HttpPost("appointments/{appointmentId:int}/attachments")]
    [Authorize(Roles = Roles.Patient)]
    [Consumes("multipart/form-data")]
    [RequestSizeLimit(AttachmentRules.MaxSizeBytes + 1024 * 1024)] // الحد + هامش لغلاف الـ multipart
    [ProducesResponseType(typeof(AttachmentDto), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Upload(int appointmentId, IFormFile file, CancellationToken ct)
    {
        await using var stream = file.OpenReadStream();
        var dto = await _service.UploadAsync(appointmentId, file.FileName, file.ContentType, stream, ct);
        return CreatedAtAction(nameof(Download), new { id = dto.Id }, dto);
    }

    /// تحميل مرفق: المريض صاحبه، أو دكتور معاه موعد (Confirmed أو CheckedIn أو Completed).
    [HttpGet("attachments/{id:int}")]
    [Authorize(Roles = Roles.Patient + "," + Roles.Doctor)]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    [ProducesResponseType(typeof(FileStreamResult), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Download(int id, CancellationToken ct)
    {
        var file = await _service.DownloadAsync(id, ct);

        // المتصفح ما يخمّنش نوع الملف، ويتحمّل كملف (Content-Disposition: attachment)
        Response.Headers["X-Content-Type-Options"] = "nosniff";
        return File(file.Content, file.ContentType, file.FileName);
    }
}

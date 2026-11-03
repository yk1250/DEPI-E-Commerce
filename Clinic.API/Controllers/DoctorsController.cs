using Clinic.Application.Common.Models;
using Clinic.Application.Common.Security;
using Clinic.Application.Features.Doctors;
using Clinic.Application.Features.Schedules;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Clinic.API.Controllers;

/// الجزء العام (البحث والملف) + الدكتور لنفسه (/me).
/// ملحوظة لمحرك الحجز: endpoints الـ slots والمواعيد تتكتب في controller تاني بـ route صريح،
/// ومتتحطش هنا.
[ApiController]
[Route("api/doctors")]
public class DoctorsController : ControllerBase
{
    private readonly IDoctorService _doctors;
    private readonly IScheduleService _schedules;

    public DoctorsController(IDoctorService doctors, IScheduleService schedules)
    {
        _doctors = doctors;
        _schedules = schedules;
    }

    // ------------------------------------------------------------------ عام

    /// بحث وفلترة الدكاترة المعتمدين: التخصص، العيادة، السعر، التقييم، الاسم، مع ترتيب وصفحات.
    [HttpGet]
    [AllowAnonymous]
    [ProducesResponseType(typeof(PagedResult<DoctorDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Search([FromQuery] DoctorSearchQuery query, CancellationToken ct)
        => Ok(await _doctors.SearchAsync(query, ct));

    /// ملف دكتور معتمد. غير المعتمد بيرجّع 404.
    [HttpGet("{id:int}", Name = "GetDoctor")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(DoctorDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Get(int id, CancellationToken ct)
        => Ok(await _doctors.GetPublicAsync(id, ct));

    /// جدول العمل الأسبوعي لدكتور معتمد (الفترات الشغالة بس).
    [HttpGet("{id:int}/schedule")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(IReadOnlyList<WorkingScheduleItemDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetSchedule(int id, CancellationToken ct)
        => Ok(await _schedules.GetPublicScheduleAsync(id, ct));

    // ------------------------------------------------------------------ الدكتور لنفسه

    [HttpGet("me")]
    [Authorize(Roles = Roles.Doctor)]
    [ProducesResponseType(typeof(AdminDoctorDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> GetMe(CancellationToken ct) => Ok(await _doctors.GetMeAsync(ct));

    /// تعديل السعر ونسبة المتابعة ومدتها والنبذة والخبرة.
    [HttpPut("me")]
    [Authorize(Roles = Roles.Doctor)]
    [ProducesResponseType(typeof(AdminDoctorDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> UpdateMe([FromBody] UpdateDoctorProfileRequest request, CancellationToken ct)
        => Ok(await _doctors.UpdateMeAsync(request, ct));

    [HttpGet("me/schedule")]
    [Authorize(Roles = Roles.Doctor)]
    [ProducesResponseType(typeof(IReadOnlyList<WorkingScheduleItemDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> GetMySchedule(CancellationToken ct) => Ok(await _schedules.GetMyScheduleAsync(ct));

    /// بيستبدل الجدول الأسبوعي كله. الفترات في نفس اليوم ماينفعش تتقاطع.
    [HttpPut("me/schedule")]
    [Authorize(Roles = Roles.Doctor)]
    [ProducesResponseType(typeof(IReadOnlyList<WorkingScheduleItemDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> ReplaceMySchedule([FromBody] SaveScheduleRequest request, CancellationToken ct)
        => Ok(await _schedules.ReplaceMyScheduleAsync(request, ct));

    [HttpGet("me/timeoffs")]
    [Authorize(Roles = Roles.Doctor)]
    [ProducesResponseType(typeof(IReadOnlyList<TimeOffDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> ListMyTimeOffs([FromQuery] bool upcomingOnly = false, CancellationToken ct = default)
        => Ok(await _schedules.ListMyTimeOffsAsync(upcomingOnly, ct));

    /// بيرجّع 409 لو الفترة بتتقاطع مع إجازة تانية أو مع مواعيد محجوزة.
    [HttpPost("me/timeoffs")]
    [Authorize(Roles = Roles.Doctor)]
    [ProducesResponseType(typeof(TimeOffDto), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> CreateMyTimeOff([FromBody] CreateTimeOffRequest request, CancellationToken ct)
    {
        var created = await _schedules.CreateMyTimeOffAsync(request, ct);
        return StatusCode(StatusCodes.Status201Created, created);
    }

    [HttpDelete("me/timeoffs/{id:int}")]
    [Authorize(Roles = Roles.Doctor)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> DeleteMyTimeOff(int id, CancellationToken ct)
    {
        await _schedules.DeleteMyTimeOffAsync(id, ct);
        return NoContent();
    }
}

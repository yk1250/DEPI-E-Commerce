using Clinic.Application.Common.Models;
using Clinic.Application.Common.Security;
using Clinic.Application.Features.Doctors;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Clinic.API.Controllers;

/// إدارة الأطباء (أدمن بس): إنشاء الحساب، الاعتماد، التعديل، وعرض كل الأطباء حتى غير المعتمدين.
[ApiController]
[Route("api/admin/doctors")]
[Authorize(Roles = Roles.Admin)]
[ProducesResponseType(StatusCodes.Status401Unauthorized)]
[ProducesResponseType(StatusCodes.Status403Forbidden)]
public class AdminDoctorsController : ControllerBase
{
    private readonly IDoctorService _doctors;

    public AdminDoctorsController(IDoctorService doctors)
    {
        _doctors = doctors;
    }

    /// بيعمل حساب دكتور + ملفه. بيتعمل غير معتمد (IsApproved = false) لحد ما الأدمن يعتمده.
    [HttpPost]
    [ProducesResponseType(typeof(AdminDoctorDto), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Create([FromBody] CreateDoctorRequest request, CancellationToken ct)
    {
        var created = await _doctors.CreateAsync(request, ct);
        return CreatedAtRoute("GetAdminDoctor", new { id = created.Id }, created);
    }

    [HttpGet]
    [ProducesResponseType(typeof(PagedResult<AdminDoctorDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> List([FromQuery] AdminDoctorQuery query, CancellationToken ct)
        => Ok(await _doctors.AdminListAsync(query, ct));

    [HttpGet("{id:int}", Name = "GetAdminDoctor")]
    [ProducesResponseType(typeof(AdminDoctorDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Get(int id, CancellationToken ct)
        => Ok(await _doctors.AdminGetAsync(id, ct));

    /// تغيير التخصص أو العيادة.
    [HttpPut("{id:int}")]
    [ProducesResponseType(typeof(AdminDoctorDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Update(int id, [FromBody] UpdateDoctorAdminRequest request, CancellationToken ct)
        => Ok(await _doctors.AdminUpdateAsync(id, request, ct));

    /// اعتماد الدكتور: بيظهر في البحث.
    [HttpPatch("{id:int}/approve")]
    [ProducesResponseType(typeof(AdminDoctorDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Approve(int id, CancellationToken ct)
        => Ok(await _doctors.SetApprovalAsync(id, true, ct));

    /// سحب الاعتماد: بيختفي من البحث (من غير مسح أي بيانات).
    [HttpPatch("{id:int}/unapprove")]
    [ProducesResponseType(typeof(AdminDoctorDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Unapprove(int id, CancellationToken ct)
        => Ok(await _doctors.SetApprovalAsync(id, false, ct));
}

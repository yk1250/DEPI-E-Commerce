using System.Security.Claims;
using Clinic.Application.Common.Interfaces;
using Clinic.Application.Common.Security;

namespace Clinic.API.Services;

/// بيقرا هوية المستخدم من الـ claims اللي في الـ JWT (التنفيذ الحقيقي لـ ICurrentUser).
public class CurrentUser : ICurrentUser
{
    private readonly IHttpContextAccessor _accessor;

    public CurrentUser(IHttpContextAccessor accessor)
    {
        _accessor = accessor;
    }

    private ClaimsPrincipal? Principal => _accessor.HttpContext?.User;

    public bool IsAuthenticated => Principal?.Identity?.IsAuthenticated == true;

    public Guid? UserId => Guid.TryParse(Principal?.FindFirst("sub")?.Value, out var id) ? id : null;

    public string? Role => Principal?.FindFirst("role")?.Value;

    public int? PatientId => ReadInt(CustomClaims.PatientId);

    public int? DoctorId => ReadInt(CustomClaims.DoctorId);

    public int? ClinicId => ReadInt(CustomClaims.ClinicId);

    public bool IsInRole(string role) => Principal?.IsInRole(role) == true;

    private int? ReadInt(string claimType) =>
        int.TryParse(Principal?.FindFirst(claimType)?.Value, out var value) ? value : null;
}

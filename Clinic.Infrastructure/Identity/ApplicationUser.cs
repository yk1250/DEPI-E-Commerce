using Clinic.Domain.Enums;
using Microsoft.AspNetCore.Identity;

namespace Clinic.Infrastructure.Identity;

public class ApplicationUser : IdentityUser<Guid>
{
    public string FullName { get; set; } = string.Empty;
    public PreferredLanguage PreferredLanguage { get; set; } = PreferredLanguage.Ar;
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

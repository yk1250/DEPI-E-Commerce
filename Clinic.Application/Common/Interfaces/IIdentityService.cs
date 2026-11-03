using Clinic.Domain.Enums;

namespace Clinic.Application.Common.Interfaces;

public record NewUser(
    string Email,
    string Password,
    string FullName,
    string? PhoneNumber,
    string Role,
    PreferredLanguage Language,
    bool EmailConfirmed);

public record AuthenticatedUser(Guid UserId, string Email, string FullName, string Role);

/// بيخبّي ASP.NET Identity عن الـ Application. التنفيذ في Clinic.Infrastructure/Identity.
public interface IIdentityService
{
    /// بيعمل الحساب ويضيفه للدور. ConflictException لو الإيميل مسجل، وAppValidationException لو الباسورد ضعيف.
    Task<Guid> CreateUserAsync(NewUser user, CancellationToken ct = default);

    /// UnauthorizedException لو الإيميل أو الباسورد غلط أو الحساب مقفول أو غير مفعّل.
    Task<AuthenticatedUser> AuthenticateAsync(string email, string password, CancellationToken ct = default);

    Task DeleteUserAsync(Guid userId, CancellationToken ct = default);
}

using Clinic.Application.Common.Exceptions;
using Clinic.Application.Common.Interfaces;
using Microsoft.AspNetCore.Identity;

namespace Clinic.Infrastructure.Identity;

public class IdentityService : IIdentityService
{
    private const string InvalidCredentials = "Invalid email or password.";

    private readonly UserManager<ApplicationUser> _users;

    public IdentityService(UserManager<ApplicationUser> users)
    {
        _users = users;
    }

    public async Task<Guid> CreateUserAsync(NewUser newUser, CancellationToken ct = default)
    {
        if (await _users.FindByEmailAsync(newUser.Email) is not null)
        {
            throw new ConflictException("An account with this email already exists.");
        }

        var user = new ApplicationUser
        {
            Id = Guid.NewGuid(),
            UserName = newUser.Email,
            Email = newUser.Email,
            EmailConfirmed = newUser.EmailConfirmed,
            PhoneNumber = newUser.PhoneNumber,
            FullName = newUser.FullName,
            PreferredLanguage = newUser.Language,
            IsActive = true,
            CreatedAt = DateTime.UtcNow
        };

        var created = await _users.CreateAsync(user, newUser.Password);
        if (!created.Succeeded)
        {
            throw ToException(created);
        }

        var addedToRole = await _users.AddToRoleAsync(user, newUser.Role);
        if (!addedToRole.Succeeded)
        {
            throw ToException(addedToRole);
        }

        return user.Id;
    }

    public async Task<AuthenticatedUser> AuthenticateAsync(string email, string password, CancellationToken ct = default)
    {
        var user = await _users.FindByEmailAsync(email);
        if (user is null || !user.IsActive)
        {
            throw new UnauthorizedException(InvalidCredentials);
        }

        if (await _users.IsLockedOutAsync(user))
        {
            throw new UnauthorizedException("This account is temporarily locked because of repeated failed logins. Try again later.");
        }

        if (!await _users.CheckPasswordAsync(user, password))
        {
            await _users.AccessFailedAsync(user);
            throw new UnauthorizedException(InvalidCredentials);
        }

        await _users.ResetAccessFailedCountAsync(user);

        var roles = await _users.GetRolesAsync(user);
        var role = roles.FirstOrDefault()
            ?? throw new UnauthorizedException("This account has no role assigned.");

        return new AuthenticatedUser(user.Id, user.Email ?? email, user.FullName, role);
    }

    public async Task DeleteUserAsync(Guid userId, CancellationToken ct = default)
    {
        var user = await _users.FindByIdAsync(userId.ToString());
        if (user is null)
        {
            return;
        }

        var result = await _users.DeleteAsync(user);
        if (!result.Succeeded)
        {
            throw new InvalidOperationException(
                "Could not delete the user: " + string.Join("; ", result.Errors.Select(e => e.Description)));
        }
    }

    private static Exception ToException(IdentityResult result)
    {
        if (result.Errors.Any(e => e.Code is "DuplicateEmail" or "DuplicateUserName"))
        {
            return new ConflictException("An account with this email already exists.");
        }

        var errors = result.Errors
            .GroupBy(e => e.Code)
            .ToDictionary(g => g.Key, g => g.Select(e => e.Description).ToArray());
        return new AppValidationException(errors);
    }
}

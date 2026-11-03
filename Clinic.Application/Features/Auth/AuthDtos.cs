using System.ComponentModel.DataAnnotations;
using Clinic.Domain.Enums;

namespace Clinic.Application.Features.Auth;

public class RegisterPatientRequest
{
    [Required, EmailAddress, StringLength(256)]
    public string Email { get; init; } = string.Empty;

    /// 8 حروف على الأقل، فيها حرف كبير وحرف صغير ورقم.
    [Required, StringLength(100, MinimumLength = 8)]
    public string Password { get; init; } = string.Empty;

    [Required, StringLength(150, MinimumLength = 2)]
    public string FullName { get; init; } = string.Empty;

    [RegularExpression(@"^\+?[0-9]{8,15}$", ErrorMessage = "Phone number must be 8 to 15 digits, with an optional leading +.")]
    public string? PhoneNumber { get; init; }

    /// yyyy-MM-dd
    [Required]
    public DateOnly? DateOfBirth { get; init; }

    /// Male | Female
    [Required]
    public Gender? Gender { get; init; }

    /// Ar | En
    public PreferredLanguage PreferredLanguage { get; init; } = PreferredLanguage.Ar;
}

public class RegisterResponse
{
    public Guid UserId { get; init; }
    public int PatientId { get; init; }
    public string Email { get; init; } = string.Empty;
    public string FullName { get; init; } = string.Empty;
}

public class LoginRequest
{
    [Required, EmailAddress]
    public string Email { get; init; } = string.Empty;

    [Required]
    public string Password { get; init; } = string.Empty;
}

public class UserDto
{
    public Guid UserId { get; init; }
    public string Email { get; init; } = string.Empty;
    public string FullName { get; init; } = string.Empty;
    public string Role { get; init; } = string.Empty;
    public int? PatientId { get; init; }
    public int? DoctorId { get; init; }
    public int? ClinicId { get; init; }
}

public class LoginResponse
{
    public string AccessToken { get; init; } = string.Empty;
    public string TokenType { get; init; } = "Bearer";
    public DateTime ExpiresAtUtc { get; init; }
    public UserDto User { get; init; } = new();
}

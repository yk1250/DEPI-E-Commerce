namespace Clinic.Infrastructure.Identity;

/// الـ Key جاي من User Secrets (Jwt:Key)، والباقي من appsettings.json.
public record JwtSettings(string Key, string Issuer, string Audience, int ExpiryMinutes);

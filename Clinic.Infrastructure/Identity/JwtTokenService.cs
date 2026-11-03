using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Clinic.Application.Common.Interfaces;
using Clinic.Application.Common.Security;

namespace Clinic.Infrastructure.Identity;

/// JWT بخوارزمية HS256 (header.payload.signature). الـ API بيتحقق منه بـ JwtBearer.
public class JwtTokenService : ITokenService
{
    private readonly JwtSettings _settings;
    private readonly TimeProvider _time;

    public JwtTokenService(JwtSettings settings, TimeProvider time)
    {
        _settings = settings;
        _time = time;
    }

    public TokenResult CreateToken(AuthenticatedUser user, int? patientId, int? doctorId, int? clinicId)
    {
        var issuedAt = _time.GetUtcNow();
        var expires = issuedAt.AddMinutes(_settings.ExpiryMinutes);

        var header = new Dictionary<string, object>
        {
            ["alg"] = "HS256",
            ["typ"] = "JWT"
        };

        var payload = new Dictionary<string, object>
        {
            ["sub"] = user.UserId.ToString(),
            ["jti"] = Guid.NewGuid().ToString("N"),
            ["email"] = user.Email,
            ["name"] = user.FullName,
            ["role"] = user.Role,
            ["iss"] = _settings.Issuer,
            ["aud"] = _settings.Audience,
            ["iat"] = issuedAt.ToUnixTimeSeconds(),
            ["nbf"] = issuedAt.ToUnixTimeSeconds(),
            ["exp"] = expires.ToUnixTimeSeconds()
        };

        if (patientId.HasValue) payload[CustomClaims.PatientId] = patientId.Value;
        if (doctorId.HasValue) payload[CustomClaims.DoctorId] = doctorId.Value;
        if (clinicId.HasValue) payload[CustomClaims.ClinicId] = clinicId.Value;

        var signingInput =
            Base64Url.EncodeToString(JsonSerializer.SerializeToUtf8Bytes(header)) + "." +
            Base64Url.EncodeToString(JsonSerializer.SerializeToUtf8Bytes(payload));

        var signature = HMACSHA256.HashData(
            Encoding.UTF8.GetBytes(_settings.Key),
            Encoding.ASCII.GetBytes(signingInput));

        return new TokenResult(signingInput + "." + Base64Url.EncodeToString(signature), expires.UtcDateTime);
    }
}

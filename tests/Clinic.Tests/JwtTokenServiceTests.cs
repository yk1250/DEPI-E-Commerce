using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Clinic.Application.Common.Interfaces;
using Clinic.Application.Common.Security;
using Clinic.Infrastructure.Identity;
using Xunit;

namespace Clinic.Tests;

public class JwtTokenServiceTests
{
    private const string Key = "unit-test-key-0123456789-0123456789-abcdef";
    private static readonly DateTimeOffset Now = new(2026, 11, 1, 12, 0, 0, TimeSpan.Zero);

    private static JwtTokenService NewService(string key = Key, int minutes = 60) =>
        new(new JwtSettings(key, "TestIssuer", "TestAudience", minutes), new FixedTimeProvider(Now));

    private static AuthenticatedUser User(string role) =>
        new(Guid.Parse("11111111-1111-1111-1111-111111111111"), "user@test.local", "Test User", role);

    private static JsonElement ReadPart(string token, int index)
    {
        var bytes = Base64Url.DecodeFromChars(token.Split('.')[index]);
        using var doc = JsonDocument.Parse(bytes);
        return doc.RootElement.Clone();
    }

    [Fact]
    public void Token_has_three_parts_and_an_hs256_header()
    {
        var result = NewService().CreateToken(User(Roles.Admin), null, null, null);

        Assert.Equal(3, result.AccessToken.Split('.').Length);
        var header = ReadPart(result.AccessToken, 0);
        Assert.Equal("HS256", header.GetProperty("alg").GetString());
        Assert.Equal("JWT", header.GetProperty("typ").GetString());
    }

    [Fact]
    public void Payload_has_the_identity_claims()
    {
        var result = NewService().CreateToken(User(Roles.Admin), null, null, null);

        var payload = ReadPart(result.AccessToken, 1);
        Assert.Equal("11111111-1111-1111-1111-111111111111", payload.GetProperty("sub").GetString());
        Assert.Equal("user@test.local", payload.GetProperty("email").GetString());
        Assert.Equal("Test User", payload.GetProperty("name").GetString());
        Assert.Equal(Roles.Admin, payload.GetProperty("role").GetString());
        Assert.Equal("TestIssuer", payload.GetProperty("iss").GetString());
        Assert.Equal("TestAudience", payload.GetProperty("aud").GetString());
    }

    [Fact]
    public void Doctor_token_has_doctor_and_clinic_ids_but_no_patient_id()
    {
        var result = NewService().CreateToken(User(Roles.Doctor), patientId: null, doctorId: 7, clinicId: 2);

        var payload = ReadPart(result.AccessToken, 1);
        Assert.Equal(7, payload.GetProperty(CustomClaims.DoctorId).GetInt32());
        Assert.Equal(2, payload.GetProperty(CustomClaims.ClinicId).GetInt32());
        Assert.False(payload.TryGetProperty(CustomClaims.PatientId, out _));
    }

    [Fact]
    public void Patient_token_has_only_the_patient_id()
    {
        var result = NewService().CreateToken(User(Roles.Patient), patientId: 5, doctorId: null, clinicId: null);

        var payload = ReadPart(result.AccessToken, 1);
        Assert.Equal(5, payload.GetProperty(CustomClaims.PatientId).GetInt32());
        Assert.False(payload.TryGetProperty(CustomClaims.DoctorId, out _));
        Assert.False(payload.TryGetProperty(CustomClaims.ClinicId, out _));
    }

    [Fact]
    public void Expiry_follows_the_configured_minutes()
    {
        var result = NewService(minutes: 90).CreateToken(User(Roles.Admin), null, null, null);

        var payload = ReadPart(result.AccessToken, 1);
        var issuedAt = payload.GetProperty("iat").GetInt64();
        var expires = payload.GetProperty("exp").GetInt64();

        Assert.Equal(Now.ToUnixTimeSeconds(), issuedAt);
        Assert.Equal(90 * 60, expires - issuedAt);
        Assert.Equal(Now.AddMinutes(90).UtcDateTime, result.ExpiresAtUtc);
    }

    [Fact]
    public void Signature_is_a_valid_hmac_sha256_of_header_and_payload()
    {
        var token = NewService().CreateToken(User(Roles.Admin), null, null, null).AccessToken;

        var parts = token.Split('.');
        var expected = HMACSHA256.HashData(Encoding.UTF8.GetBytes(Key), Encoding.ASCII.GetBytes(parts[0] + "." + parts[1]));
        var actual = Base64Url.DecodeFromChars(parts[2]);

        Assert.True(CryptographicOperations.FixedTimeEquals(expected, actual));
    }

    [Fact]
    public void A_different_key_produces_a_different_signature()
    {
        var first = NewService().CreateToken(User(Roles.Admin), null, null, null).AccessToken.Split('.')[2];
        var second = NewService(key: "another-key-0123456789-0123456789-abcdef").CreateToken(User(Roles.Admin), null, null, null)
            .AccessToken.Split('.')[2];

        Assert.NotEqual(first, second);
    }

    [Fact]
    public void Every_token_gets_a_unique_jti()
    {
        var service = NewService();

        var first = ReadPart(service.CreateToken(User(Roles.Admin), null, null, null).AccessToken, 1).GetProperty("jti").GetString();
        var second = ReadPart(service.CreateToken(User(Roles.Admin), null, null, null).AccessToken, 1).GetProperty("jti").GetString();

        Assert.NotEqual(first, second);
    }
}

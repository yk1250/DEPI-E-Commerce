using System.Security.Claims;
using Clinic.API.Services;
using Clinic.Application.Common.Security;
using Microsoft.AspNetCore.Http;
using Xunit;

namespace Clinic.Tests;

public class CurrentUserTests
{
    private static CurrentUser For(params Claim[] claims)
    {
        // نفس إعدادات الـ API: النوع "role" والاسم "name" (MapInboundClaims = false)
        var identity = new ClaimsIdentity(claims, authenticationType: "test", nameType: "name", roleType: "role");
        var accessor = new HttpContextAccessor
        {
            HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal(identity) }
        };
        return new CurrentUser(accessor);
    }

    [Fact]
    public void Doctor_claims_are_read_correctly()
    {
        var userId = Guid.NewGuid();
        var user = For(
            new Claim("sub", userId.ToString()),
            new Claim("role", Roles.Doctor),
            new Claim(CustomClaims.DoctorId, "4"),
            new Claim(CustomClaims.ClinicId, "2"));

        Assert.True(user.IsAuthenticated);
        Assert.Equal(userId, user.UserId);
        Assert.Equal(Roles.Doctor, user.Role);
        Assert.Equal(4, user.DoctorId);
        Assert.Equal(2, user.ClinicId);
        Assert.Null(user.PatientId);
        Assert.True(user.IsInRole(Roles.Doctor));
        Assert.False(user.IsInRole(Roles.Admin));
    }

    [Fact]
    public void Patient_claims_are_read_correctly()
    {
        var user = For(
            new Claim("sub", Guid.NewGuid().ToString()),
            new Claim("role", Roles.Patient),
            new Claim(CustomClaims.PatientId, "9"));

        Assert.Equal(9, user.PatientId);
        Assert.Null(user.DoctorId);
        Assert.Null(user.ClinicId);
    }

    [Fact]
    public void Request_without_http_context_is_anonymous()
    {
        var user = new CurrentUser(new HttpContextAccessor());

        Assert.False(user.IsAuthenticated);
        Assert.Null(user.UserId);
        Assert.Null(user.Role);
        Assert.Null(user.DoctorId);
        Assert.False(user.IsInRole(Roles.Admin));
    }

    [Fact]
    public void Empty_identity_is_not_authenticated()
    {
        var accessor = new HttpContextAccessor
        {
            HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity()) }
        };

        Assert.False(new CurrentUser(accessor).IsAuthenticated);
    }

    [Fact]
    public void Malformed_claim_values_become_null_instead_of_throwing()
    {
        var user = For(
            new Claim("sub", "not-a-guid"),
            new Claim(CustomClaims.DoctorId, "abc"));

        Assert.Null(user.UserId);
        Assert.Null(user.DoctorId);
    }
}

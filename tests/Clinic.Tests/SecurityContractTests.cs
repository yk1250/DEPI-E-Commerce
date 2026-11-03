using Clinic.Application.Common.Security;
using Xunit;

namespace Clinic.Tests;

/// بتثبّت الأسماء اللي باقي الفريق بيعتمد عليها (الأدوار وأسماء claims الـ JWT).
public class SecurityContractTests
{
    [Fact]
    public void There_are_exactly_four_distinct_roles()
    {
        Assert.Equal(4, Roles.All.Length);
        Assert.Equal(4, Roles.All.Distinct().Count());
        Assert.Contains(Roles.Admin, Roles.All);
        Assert.Contains(Roles.Doctor, Roles.All);
        Assert.Contains(Roles.Patient, Roles.All);
        Assert.Contains(Roles.Receptionist, Roles.All);
    }

    [Fact]
    public void Role_names_are_the_exact_strings_used_in_authorize_attributes()
    {
        Assert.Equal("Admin", Roles.Admin);
        Assert.Equal("Doctor", Roles.Doctor);
        Assert.Equal("Patient", Roles.Patient);
        Assert.Equal("Receptionist", Roles.Receptionist);
    }

    [Fact]
    public void Custom_claim_names_are_stable()
    {
        Assert.Equal("patient_id", CustomClaims.PatientId);
        Assert.Equal("doctor_id", CustomClaims.DoctorId);
        Assert.Equal("clinic_id", CustomClaims.ClinicId);
    }
}

using Clinic.Application.Common.Security;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;

namespace Clinic.Infrastructure.Identity;

/// بيشتغل مع كل تشغيل للـ API وآمن للتكرار:
/// 1) بيعمل الأدوار الأربعة لو مش موجودة.
/// 2) في Development بس: بيحط باسورد لحسابات الاختبار (@test.local) وبيربطها بأدوارها.
public static class DbSeeder
{
    /// باسورد حسابات الاختبار. للتطوير المحلي بس، ومش بيتحط إلا لو الحساب من غير باسورد.
    public const string DevPassword = "Test@12345";

    private static readonly (string Email, string Role)[] TestAccounts =
    [
        ("admin@test.local", Roles.Admin),
        ("doctor1@test.local", Roles.Doctor),
        ("doctor2@test.local", Roles.Doctor),
        ("doctor3@test.local", Roles.Doctor),
        ("patient1@test.local", Roles.Patient),
        ("patient2@test.local", Roles.Patient),
        ("patient3@test.local", Roles.Patient),
        ("reception1@test.local", Roles.Receptionist)
    ];

    public static async Task SeedAsync(IServiceProvider services, bool seedTestAccounts)
    {
        using var scope = services.CreateScope();

        var roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole<Guid>>>();
        foreach (var role in Roles.All)
        {
            if (await roleManager.RoleExistsAsync(role))
            {
                continue;
            }

            var created = await roleManager.CreateAsync(new IdentityRole<Guid>(role));
            if (!created.Succeeded)
            {
                throw new InvalidOperationException(
                    $"Could not create role '{role}': " + string.Join("; ", created.Errors.Select(e => e.Description)));
            }
        }

        if (!seedTestAccounts)
        {
            return;
        }

        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        foreach (var (email, role) in TestAccounts)
        {
            var user = await users.FindByEmailAsync(email);
            if (user is null)
            {
                continue; // الـ seed-all-tables.sql لسه ما اتشغلش
            }

            if (!await users.HasPasswordAsync(user))
            {
                var passwordResult = await users.AddPasswordAsync(user, DevPassword);
                if (!passwordResult.Succeeded)
                {
                    throw new InvalidOperationException(
                        $"Could not set the test password for {email}: " +
                        string.Join("; ", passwordResult.Errors.Select(e => e.Description)));
                }
            }

            if (!await users.IsInRoleAsync(user, role))
            {
                var roleResult = await users.AddToRoleAsync(user, role);
                if (!roleResult.Succeeded)
                {
                    throw new InvalidOperationException(
                        $"Could not add {email} to role {role}: " +
                        string.Join("; ", roleResult.Errors.Select(e => e.Description)));
                }
            }
        }
    }
}

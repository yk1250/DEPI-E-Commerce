namespace Clinic.Application.Common.Models;

/// بيانات الحساب اللي الـ Application محتاجها (الاسم والإيميل...) من غير ما يعرف ApplicationUser.
public class UserSummary
{
    public Guid Id { get; init; }
    public string FullName { get; init; } = string.Empty;
    public string? Email { get; init; }
    public string? PhoneNumber { get; init; }
    public bool IsActive { get; init; }
}

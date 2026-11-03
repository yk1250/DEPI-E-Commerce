using System.ComponentModel.DataAnnotations;

namespace Clinic.Application.Features.Doctors;

/// اللي المريض (وأي زائر) يشوفه عن الدكتور.
public class DoctorDto
{
    public int Id { get; init; }
    public string FullName { get; init; } = string.Empty;
    public int SpecialtyId { get; init; }
    public string SpecialtyNameAr { get; init; } = string.Empty;
    public string SpecialtyNameEn { get; init; } = string.Empty;
    public int ClinicId { get; init; }
    public string ClinicName { get; init; } = string.Empty;
    public string ClinicCity { get; init; } = string.Empty;
    public string? Bio { get; init; }
    public int YearsOfExperience { get; init; }
    public decimal ConsultationFee { get; init; }
    public decimal FollowUpFeePercent { get; init; }
    public int FollowUpWindowDays { get; init; }
    public decimal AverageRating { get; init; }
    public int ReviewsCount { get; init; }
}

/// اللي الأدمن (والدكتور لنفسه) يشوفه: بيزيد بيانات الحساب وحالة الاعتماد.
public class AdminDoctorDto : DoctorDto
{
    public Guid UserId { get; init; }
    public string? Email { get; init; }
    public string? PhoneNumber { get; init; }
    public bool IsApproved { get; init; }
    public bool IsAccountActive { get; init; }
    public bool ClinicIsActive { get; init; }
}

public class DoctorSearchQuery
{
    public int? SpecialtyId { get; init; }
    public int? ClinicId { get; init; }

    [Range(0, 1_000_000)]
    public decimal? MinPrice { get; init; }

    [Range(0, 1_000_000)]
    public decimal? MaxPrice { get; init; }

    [Range(0, 5)]
    public decimal? MinRating { get; init; }

    /// بحث بجزء من اسم الدكتور
    [StringLength(100)]
    public string? Q { get; init; }

    /// rating (الافتراضي) | price | experience | name
    public string? SortBy { get; init; }

    /// asc | desc (الافتراضي: desc للتقييم، asc للباقي)
    public string? SortDir { get; init; }

    [Range(1, int.MaxValue)]
    public int Page { get; init; } = 1;

    [Range(1, 50)]
    public int PageSize { get; init; } = 10;
}

public class AdminDoctorQuery
{
    public bool? IsApproved { get; init; }
    public int? SpecialtyId { get; init; }
    public int? ClinicId { get; init; }

    [StringLength(100)]
    public string? Q { get; init; }

    [Range(1, int.MaxValue)]
    public int Page { get; init; } = 1;

    [Range(1, 100)]
    public int PageSize { get; init; } = 20;
}

public class CreateDoctorRequest
{
    [Required, EmailAddress, StringLength(256)]
    public string Email { get; init; } = string.Empty;

    /// باسورد مؤقت (8 حروف، حرف كبير وصغير ورقم)
    [Required, StringLength(100, MinimumLength = 8)]
    public string Password { get; init; } = string.Empty;

    [Required, StringLength(150, MinimumLength = 2)]
    public string FullName { get; init; } = string.Empty;

    [RegularExpression(@"^\+?[0-9]{8,15}$", ErrorMessage = "Phone number must be 8 to 15 digits, with an optional leading +.")]
    public string? PhoneNumber { get; init; }

    [Range(1, int.MaxValue)]
    public int SpecialtyId { get; init; }

    [Range(1, int.MaxValue)]
    public int ClinicId { get; init; }

    [StringLength(1000)]
    public string? Bio { get; init; }

    [Range(0, 70)]
    public int YearsOfExperience { get; init; }

    [Range(0, 100000)]
    public decimal ConsultationFee { get; init; }

    /// نسبة سعر المتابعة من السعر الأساسي (0 = مجاني، 100 = نفس السعر)
    [Range(0, 100)]
    public decimal FollowUpFeePercent { get; init; }

    [Range(0, 365)]
    public int FollowUpWindowDays { get; init; }
}

public class UpdateDoctorAdminRequest
{
    [Range(1, int.MaxValue)]
    public int SpecialtyId { get; init; }

    [Range(1, int.MaxValue)]
    public int ClinicId { get; init; }
}

/// الدكتور بيعدّل بياناته بنفسه (من غير التخصص والعيادة والاعتماد).
public class UpdateDoctorProfileRequest
{
    [StringLength(1000)]
    public string? Bio { get; init; }

    [Range(0, 70)]
    public int YearsOfExperience { get; init; }

    [Range(0, 100000)]
    public decimal ConsultationFee { get; init; }

    [Range(0, 100)]
    public decimal FollowUpFeePercent { get; init; }

    [Range(0, 365)]
    public int FollowUpWindowDays { get; init; }
}

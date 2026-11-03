using Clinic.Application.Common.Exceptions;
using Clinic.Application.Common.Interfaces;
using Clinic.Application.Common.Models;
using Clinic.Application.Common.Security;
using Clinic.Domain.Entities;
using Clinic.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace Clinic.Application.Features.Doctors;

public interface IDoctorService
{
    // عام (أي زائر): الدكاترة المعتمدين بس
    Task<PagedResult<DoctorDto>> SearchAsync(DoctorSearchQuery query, CancellationToken ct);
    Task<DoctorDto> GetPublicAsync(int id, CancellationToken ct);

    // الدكتور لنفسه
    Task<AdminDoctorDto> GetMeAsync(CancellationToken ct);
    Task<AdminDoctorDto> UpdateMeAsync(UpdateDoctorProfileRequest request, CancellationToken ct);

    // الأدمن
    Task<AdminDoctorDto> CreateAsync(CreateDoctorRequest request, CancellationToken ct);
    Task<PagedResult<AdminDoctorDto>> AdminListAsync(AdminDoctorQuery query, CancellationToken ct);
    Task<AdminDoctorDto> AdminGetAsync(int id, CancellationToken ct);
    Task<AdminDoctorDto> AdminUpdateAsync(int id, UpdateDoctorAdminRequest request, CancellationToken ct);
    Task<AdminDoctorDto> SetApprovalAsync(int id, bool approved, CancellationToken ct);
}

public class DoctorService : IDoctorService
{
    private static readonly string[] AllowedSortFields = ["rating", "price", "experience", "name"];

    private readonly IAppDbContext _db;
    private readonly IIdentityService _identity;
    private readonly ICurrentUser _currentUser;

    public DoctorService(IAppDbContext db, IIdentityService identity, ICurrentUser currentUser)
    {
        _db = db;
        _identity = identity;
        _currentUser = currentUser;
    }

    // ------------------------------------------------------------------ عام

    public async Task<PagedResult<DoctorDto>> SearchAsync(DoctorSearchQuery q, CancellationToken ct)
    {
        if (q.MinPrice.HasValue && q.MaxPrice.HasValue && q.MinPrice.Value > q.MaxPrice.Value)
        {
            throw new AppValidationException(new Dictionary<string, string[]>
            {
                ["minPrice"] = ["minPrice must be less than or equal to maxPrice."]
            });
        }

        var sortKey = (q.SortBy ?? "rating").Trim().ToLowerInvariant();
        if (!AllowedSortFields.Contains(sortKey))
        {
            throw new AppValidationException(new Dictionary<string, string[]>
            {
                ["sortBy"] = [$"sortBy must be one of: {string.Join(", ", AllowedSortFields)}."]
            });
        }

        if (q.SortDir is not null &&
            !q.SortDir.Equals("asc", StringComparison.OrdinalIgnoreCase) &&
            !q.SortDir.Equals("desc", StringComparison.OrdinalIgnoreCase))
        {
            throw new AppValidationException(new Dictionary<string, string[]>
            {
                ["sortDir"] = ["sortDir must be asc or desc."]
            });
        }

        // الدكتور يظهر في البحث لو: معتمد + حسابه شغال + عيادته شغالة
        var query = DoctorQuery().Where(d => d.IsApproved && d.IsAccountActive && d.ClinicIsActive);

        if (q.SpecialtyId.HasValue)
        {
            var specialtyId = q.SpecialtyId.Value;
            query = query.Where(d => d.SpecialtyId == specialtyId);
        }

        if (q.ClinicId.HasValue)
        {
            var clinicId = q.ClinicId.Value;
            query = query.Where(d => d.ClinicId == clinicId);
        }

        if (q.MinPrice.HasValue)
        {
            var minPrice = q.MinPrice.Value;
            query = query.Where(d => d.ConsultationFee >= minPrice);
        }

        if (q.MaxPrice.HasValue)
        {
            var maxPrice = q.MaxPrice.Value;
            query = query.Where(d => d.ConsultationFee <= maxPrice);
        }

        if (q.MinRating.HasValue)
        {
            var minRating = q.MinRating.Value;
            query = query.Where(d => d.AverageRating >= minRating);
        }

        if (!string.IsNullOrWhiteSpace(q.Q))
        {
            var term = q.Q.Trim();
            query = query.Where(d => d.FullName.Contains(term));
        }

        query = ApplySort(query, sortKey, q.SortDir);

        var total = await query.CountAsync(ct);
        var rows = await query.Skip((q.Page - 1) * q.PageSize).Take(q.PageSize).ToListAsync(ct);

        return new PagedResult<DoctorDto>
        {
            Items = rows.Select(ToPublic).ToList(),
            Page = q.Page,
            PageSize = q.PageSize,
            TotalCount = total
        };
    }

    public async Task<DoctorDto> GetPublicAsync(int id, CancellationToken ct)
    {
        var doctor = await DoctorQuery().FirstOrDefaultAsync(d => d.Id == id, ct);

        // غير المعتمد 404 مش 403، عشان ما نكشفش إنه موجود
        if (doctor is null || !doctor.IsApproved || !doctor.IsAccountActive || !doctor.ClinicIsActive)
        {
            throw new NotFoundException("Doctor", id);
        }

        return ToPublic(doctor);
    }

    // ------------------------------------------------------------------ الدكتور لنفسه

    public Task<AdminDoctorDto> GetMeAsync(CancellationToken ct) => AdminGetAsync(RequireDoctorId(), ct);

    public async Task<AdminDoctorDto> UpdateMeAsync(UpdateDoctorProfileRequest request, CancellationToken ct)
    {
        var doctorId = RequireDoctorId();
        var doctor = await _db.Doctors.FirstOrDefaultAsync(d => d.Id == doctorId, ct)
            ?? throw new NotFoundException("Doctor", doctorId);

        // تغيير السعر مش بيأثر على المواعيد القديمة (السعر محفوظ في PriceSnapshot)
        doctor.Bio = string.IsNullOrWhiteSpace(request.Bio) ? null : request.Bio.Trim();
        doctor.YearsOfExperience = request.YearsOfExperience;
        doctor.ConsultationFee = request.ConsultationFee;
        doctor.FollowUpFeePercent = request.FollowUpFeePercent;
        doctor.FollowUpWindowDays = request.FollowUpWindowDays;
        await _db.SaveChangesAsync(ct);

        return await AdminGetAsync(doctorId, ct);
    }

    // ------------------------------------------------------------------ الأدمن

    public async Task<AdminDoctorDto> CreateAsync(CreateDoctorRequest request, CancellationToken ct)
    {
        await EnsureSpecialtyAndClinicAsync(request.SpecialtyId, request.ClinicId, ct);

        // الحساب وملف الدكتور في Transaction واحدة
        await using var tx = await _db.Database.BeginTransactionAsync(ct);

        var userId = await _identity.CreateUserAsync(new NewUser(
            request.Email.Trim().ToLowerInvariant(),
            request.Password,
            request.FullName.Trim(),
            request.PhoneNumber,
            Roles.Doctor,
            PreferredLanguage.Ar,
            EmailConfirmed: true), ct);

        var doctor = new Doctor
        {
            UserId = userId,
            SpecialtyId = request.SpecialtyId,
            ClinicId = request.ClinicId,
            Bio = string.IsNullOrWhiteSpace(request.Bio) ? null : request.Bio.Trim(),
            YearsOfExperience = request.YearsOfExperience,
            ConsultationFee = request.ConsultationFee,
            FollowUpFeePercent = request.FollowUpFeePercent,
            FollowUpWindowDays = request.FollowUpWindowDays,
            IsApproved = false
        };
        _db.Doctors.Add(doctor);
        await _db.SaveChangesAsync(ct);

        await tx.CommitAsync(ct);

        return await AdminGetAsync(doctor.Id, ct);
    }

    public async Task<PagedResult<AdminDoctorDto>> AdminListAsync(AdminDoctorQuery q, CancellationToken ct)
    {
        var query = DoctorQuery();

        if (q.IsApproved.HasValue)
        {
            var isApproved = q.IsApproved.Value;
            query = query.Where(d => d.IsApproved == isApproved);
        }

        if (q.SpecialtyId.HasValue)
        {
            var specialtyId = q.SpecialtyId.Value;
            query = query.Where(d => d.SpecialtyId == specialtyId);
        }

        if (q.ClinicId.HasValue)
        {
            var clinicId = q.ClinicId.Value;
            query = query.Where(d => d.ClinicId == clinicId);
        }

        if (!string.IsNullOrWhiteSpace(q.Q))
        {
            var term = q.Q.Trim();
            query = query.Where(d => d.FullName.Contains(term));
        }

        var total = await query.CountAsync(ct);
        var rows = await query.OrderBy(d => d.Id)
            .Skip((q.Page - 1) * q.PageSize).Take(q.PageSize)
            .ToListAsync(ct);

        return new PagedResult<AdminDoctorDto>
        {
            Items = rows,
            Page = q.Page,
            PageSize = q.PageSize,
            TotalCount = total
        };
    }

    public async Task<AdminDoctorDto> AdminGetAsync(int id, CancellationToken ct)
    {
        return await DoctorQuery().FirstOrDefaultAsync(d => d.Id == id, ct)
            ?? throw new NotFoundException("Doctor", id);
    }

    public async Task<AdminDoctorDto> AdminUpdateAsync(int id, UpdateDoctorAdminRequest request, CancellationToken ct)
    {
        var doctor = await _db.Doctors.FirstOrDefaultAsync(d => d.Id == id, ct)
            ?? throw new NotFoundException("Doctor", id);

        await EnsureSpecialtyAndClinicAsync(request.SpecialtyId, request.ClinicId, ct);

        doctor.SpecialtyId = request.SpecialtyId;
        doctor.ClinicId = request.ClinicId;
        await _db.SaveChangesAsync(ct);

        return await AdminGetAsync(id, ct);
    }

    public async Task<AdminDoctorDto> SetApprovalAsync(int id, bool approved, CancellationToken ct)
    {
        var doctor = await _db.Doctors.FirstOrDefaultAsync(d => d.Id == id, ct)
            ?? throw new NotFoundException("Doctor", id);

        doctor.IsApproved = approved;
        await _db.SaveChangesAsync(ct);

        return await AdminGetAsync(id, ct);
    }

    // ------------------------------------------------------------------ مساعدات

    private int RequireDoctorId() =>
        _currentUser.DoctorId ?? throw new ForbiddenException("This account is not linked to a doctor profile.");

    private async Task EnsureSpecialtyAndClinicAsync(int specialtyId, int clinicId, CancellationToken ct)
    {
        var errors = new Dictionary<string, string[]>();

        if (!await _db.Specialties.AnyAsync(s => s.Id == specialtyId, ct))
        {
            errors["specialtyId"] = ["Specialty was not found."];
        }

        var clinic = await _db.Clinics.AsNoTracking().FirstOrDefaultAsync(c => c.Id == clinicId, ct);
        if (clinic is null)
        {
            errors["clinicId"] = ["Clinic was not found."];
        }
        else if (!clinic.IsActive)
        {
            errors["clinicId"] = ["Clinic is not active."];
        }

        if (errors.Count > 0)
        {
            throw new AppValidationException(errors);
        }
    }

    /// الاستعلام الأساسي: الدكتور + بيانات حسابه + التخصص + العيادة.
    private IQueryable<AdminDoctorDto> DoctorQuery() =>
        from d in _db.Doctors.AsNoTracking()
        join u in _db.UserSummaries on d.UserId equals u.Id
        select new AdminDoctorDto
        {
            Id = d.Id,
            UserId = d.UserId,
            FullName = u.FullName,
            Email = u.Email,
            PhoneNumber = u.PhoneNumber,
            IsAccountActive = u.IsActive,
            SpecialtyId = d.SpecialtyId,
            SpecialtyNameAr = d.Specialty.NameAr,
            SpecialtyNameEn = d.Specialty.NameEn,
            ClinicId = d.ClinicId,
            ClinicName = d.Clinic.Name,
            ClinicCity = d.Clinic.City,
            ClinicIsActive = d.Clinic.IsActive,
            Bio = d.Bio,
            YearsOfExperience = d.YearsOfExperience,
            ConsultationFee = d.ConsultationFee,
            FollowUpFeePercent = d.FollowUpFeePercent,
            FollowUpWindowDays = d.FollowUpWindowDays,
            AverageRating = d.AverageRating,
            ReviewsCount = d.ReviewsCount,
            IsApproved = d.IsApproved
        };

    private static IQueryable<AdminDoctorDto> ApplySort(IQueryable<AdminDoctorDto> source, string sortKey, string? sortDir)
    {
        var descending = sortDir is null
            ? sortKey == "rating"
            : sortDir.Equals("desc", StringComparison.OrdinalIgnoreCase);

        return sortKey switch
        {
            "price" => descending
                ? source.OrderByDescending(d => d.ConsultationFee).ThenBy(d => d.Id)
                : source.OrderBy(d => d.ConsultationFee).ThenBy(d => d.Id),
            "experience" => descending
                ? source.OrderByDescending(d => d.YearsOfExperience).ThenBy(d => d.Id)
                : source.OrderBy(d => d.YearsOfExperience).ThenBy(d => d.Id),
            "name" => descending
                ? source.OrderByDescending(d => d.FullName).ThenBy(d => d.Id)
                : source.OrderBy(d => d.FullName).ThenBy(d => d.Id),
            _ => descending
                ? source.OrderByDescending(d => d.AverageRating).ThenByDescending(d => d.ReviewsCount).ThenBy(d => d.Id)
                : source.OrderBy(d => d.AverageRating).ThenBy(d => d.ReviewsCount).ThenBy(d => d.Id)
        };
    }

    private static DoctorDto ToPublic(AdminDoctorDto d) => new()
    {
        Id = d.Id,
        FullName = d.FullName,
        SpecialtyId = d.SpecialtyId,
        SpecialtyNameAr = d.SpecialtyNameAr,
        SpecialtyNameEn = d.SpecialtyNameEn,
        ClinicId = d.ClinicId,
        ClinicName = d.ClinicName,
        ClinicCity = d.ClinicCity,
        Bio = d.Bio,
        YearsOfExperience = d.YearsOfExperience,
        ConsultationFee = d.ConsultationFee,
        FollowUpFeePercent = d.FollowUpFeePercent,
        FollowUpWindowDays = d.FollowUpWindowDays,
        AverageRating = d.AverageRating,
        ReviewsCount = d.ReviewsCount
    };
}

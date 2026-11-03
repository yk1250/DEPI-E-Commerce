using System.ComponentModel.DataAnnotations;
using Clinic.Application.Common.Exceptions;
using Clinic.Application.Common.Interfaces;
using Clinic.Application.Common.Security;
using Clinic.Domain.Entities;
using Clinic.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace Clinic.Application.Features.Receptionists;

public class ReceptionistDto
{
    public int Id { get; init; }
    public Guid UserId { get; init; }
    public string FullName { get; init; } = string.Empty;
    public string? Email { get; init; }
    public string? PhoneNumber { get; init; }
    public bool IsAccountActive { get; init; }
    public int ClinicId { get; init; }
    public string ClinicName { get; init; } = string.Empty;
}

public class CreateReceptionistRequest
{
    [Required, EmailAddress, StringLength(256)]
    public string Email { get; init; } = string.Empty;

    [Required, StringLength(100, MinimumLength = 8)]
    public string Password { get; init; } = string.Empty;

    [Required, StringLength(150, MinimumLength = 2)]
    public string FullName { get; init; } = string.Empty;

    [RegularExpression(@"^\+?[0-9]{8,15}$", ErrorMessage = "Phone number must be 8 to 15 digits, with an optional leading +.")]
    public string? PhoneNumber { get; init; }

    [Range(1, int.MaxValue)]
    public int ClinicId { get; init; }
}

public interface IReceptionistService
{
    Task<ReceptionistDto> CreateAsync(CreateReceptionistRequest request, CancellationToken ct);
    Task<IReadOnlyList<ReceptionistDto>> ListAsync(int? clinicId, CancellationToken ct);
    Task<ReceptionistDto> GetAsync(int id, CancellationToken ct);
    Task DeleteAsync(int id, CancellationToken ct);
}

public class ReceptionistService : IReceptionistService
{
    private readonly IAppDbContext _db;
    private readonly IIdentityService _identity;

    public ReceptionistService(IAppDbContext db, IIdentityService identity)
    {
        _db = db;
        _identity = identity;
    }

    public async Task<ReceptionistDto> CreateAsync(CreateReceptionistRequest request, CancellationToken ct)
    {
        var clinic = await _db.Clinics.AsNoTracking().FirstOrDefaultAsync(c => c.Id == request.ClinicId, ct);
        if (clinic is null || !clinic.IsActive)
        {
            throw new AppValidationException(new Dictionary<string, string[]>
            {
                ["clinicId"] = [clinic is null ? "Clinic was not found." : "Clinic is not active."]
            });
        }

        await using var tx = await _db.Database.BeginTransactionAsync(ct);

        var userId = await _identity.CreateUserAsync(new NewUser(
            request.Email.Trim().ToLowerInvariant(),
            request.Password,
            request.FullName.Trim(),
            request.PhoneNumber,
            Roles.Receptionist,
            PreferredLanguage.Ar,
            EmailConfirmed: true), ct);

        var receptionist = new Receptionist { UserId = userId, ClinicId = request.ClinicId };
        _db.Receptionists.Add(receptionist);
        await _db.SaveChangesAsync(ct);

        await tx.CommitAsync(ct);

        return await GetAsync(receptionist.Id, ct);
    }

    public async Task<IReadOnlyList<ReceptionistDto>> ListAsync(int? clinicId, CancellationToken ct)
    {
        var query = Query();
        if (clinicId.HasValue)
        {
            var id = clinicId.Value;
            query = query.Where(r => r.ClinicId == id);
        }

        return await query.OrderBy(r => r.Id).ToListAsync(ct);
    }

    public async Task<ReceptionistDto> GetAsync(int id, CancellationToken ct)
    {
        return await Query().FirstOrDefaultAsync(r => r.Id == id, ct)
            ?? throw new NotFoundException("Receptionist", id);
    }

    public async Task DeleteAsync(int id, CancellationToken ct)
    {
        var receptionist = await _db.Receptionists.FirstOrDefaultAsync(r => r.Id == id, ct)
            ?? throw new NotFoundException("Receptionist", id);

        // مفيش جدول بيشاور على الريسبشن، فمسموح نمسح الصف والحساب. لو الحساب ليه سجلات تانية بيرجع 409.
        await using var tx = await _db.Database.BeginTransactionAsync(ct);

        var userId = receptionist.UserId;
        _db.Receptionists.Remove(receptionist);
        await _db.SaveChangesAsync(ct);
        await _identity.DeleteUserAsync(userId, ct);

        await tx.CommitAsync(ct);
    }

    private IQueryable<ReceptionistDto> Query() =>
        from r in _db.Receptionists.AsNoTracking()
        join u in _db.UserSummaries on r.UserId equals u.Id
        join c in _db.Clinics on r.ClinicId equals c.Id
        select new ReceptionistDto
        {
            Id = r.Id,
            UserId = r.UserId,
            FullName = u.FullName,
            Email = u.Email,
            PhoneNumber = u.PhoneNumber,
            IsAccountActive = u.IsActive,
            ClinicId = r.ClinicId,
            ClinicName = c.Name
        };
}

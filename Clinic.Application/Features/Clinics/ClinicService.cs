using System.ComponentModel.DataAnnotations;
using Clinic.Application.Common.Exceptions;
using Clinic.Application.Common.Interfaces;
using Clinic.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Clinic.Application.Features.Clinics;

public class ClinicDto
{
    public int Id { get; init; }
    public string Name { get; init; } = string.Empty;
    public string Address { get; init; } = string.Empty;
    public string City { get; init; } = string.Empty;
    public string? Phone { get; init; }
    public bool IsActive { get; init; }
}

public class SaveClinicRequest
{
    [Required, StringLength(150, MinimumLength = 2)]
    public string Name { get; init; } = string.Empty;

    [Required, StringLength(300, MinimumLength = 2)]
    public string Address { get; init; } = string.Empty;

    [Required, StringLength(80, MinimumLength = 2)]
    public string City { get; init; } = string.Empty;

    [RegularExpression(@"^\+?[0-9]{8,15}$", ErrorMessage = "Phone number must be 8 to 15 digits, with an optional leading +.")]
    public string? Phone { get; init; }

    public bool IsActive { get; init; } = true;
}

public interface IClinicService
{
    Task<IReadOnlyList<ClinicDto>> ListAsync(bool includeInactive, CancellationToken ct);
    Task<ClinicDto> GetAsync(int id, CancellationToken ct);
    Task<ClinicDto> CreateAsync(SaveClinicRequest request, CancellationToken ct);
    Task<ClinicDto> UpdateAsync(int id, SaveClinicRequest request, CancellationToken ct);
    Task DeleteAsync(int id, CancellationToken ct);
}

public class ClinicService : IClinicService
{
    private readonly IAppDbContext _db;

    public ClinicService(IAppDbContext db)
    {
        _db = db;
    }

    public async Task<IReadOnlyList<ClinicDto>> ListAsync(bool includeInactive, CancellationToken ct)
    {
        var query = _db.Clinics.AsNoTracking();
        if (!includeInactive)
        {
            query = query.Where(c => c.IsActive);
        }

        return await query
            .OrderBy(c => c.City).ThenBy(c => c.Name)
            .Select(c => new ClinicDto
            {
                Id = c.Id, Name = c.Name, Address = c.Address, City = c.City, Phone = c.Phone, IsActive = c.IsActive
            })
            .ToListAsync(ct);
    }

    public async Task<ClinicDto> GetAsync(int id, CancellationToken ct)
    {
        var clinic = await _db.Clinics.AsNoTracking().FirstOrDefaultAsync(c => c.Id == id, ct)
            ?? throw new NotFoundException("Clinic", id);
        return ToDto(clinic);
    }

    public async Task<ClinicDto> CreateAsync(SaveClinicRequest request, CancellationToken ct)
    {
        var name = request.Name.Trim();
        var city = request.City.Trim();
        await EnsureNameIsFreeAsync(name, city, null, ct);

        var clinic = new MedicalClinic
        {
            Name = name,
            Address = request.Address.Trim(),
            City = city,
            Phone = request.Phone,
            IsActive = request.IsActive
        };
        _db.Clinics.Add(clinic);
        await _db.SaveChangesAsync(ct);
        return ToDto(clinic);
    }

    public async Task<ClinicDto> UpdateAsync(int id, SaveClinicRequest request, CancellationToken ct)
    {
        var clinic = await _db.Clinics.FirstOrDefaultAsync(c => c.Id == id, ct)
            ?? throw new NotFoundException("Clinic", id);

        var name = request.Name.Trim();
        var city = request.City.Trim();
        await EnsureNameIsFreeAsync(name, city, id, ct);

        clinic.Name = name;
        clinic.Address = request.Address.Trim();
        clinic.City = city;
        clinic.Phone = request.Phone;
        clinic.IsActive = request.IsActive;
        await _db.SaveChangesAsync(ct);
        return ToDto(clinic);
    }

    public async Task DeleteAsync(int id, CancellationToken ct)
    {
        var clinic = await _db.Clinics.FirstOrDefaultAsync(c => c.Id == id, ct)
            ?? throw new NotFoundException("Clinic", id);

        var inUse = await _db.Doctors.AnyAsync(d => d.ClinicId == id, ct)
                    || await _db.Receptionists.AnyAsync(r => r.ClinicId == id, ct);
        if (inUse)
        {
            throw new ConflictException(
                "This clinic has doctors or receptionists and cannot be deleted. Set IsActive to false instead.");
        }

        _db.Clinics.Remove(clinic);
        await _db.SaveChangesAsync(ct);
    }

    private async Task EnsureNameIsFreeAsync(string name, string city, int? excludeId, CancellationToken ct)
    {
        var exists = await _db.Clinics.AnyAsync(
            c => c.Name == name && c.City == city && (excludeId == null || c.Id != excludeId), ct);
        if (exists)
        {
            throw new ConflictException("A clinic with the same name already exists in this city.");
        }
    }

    private static ClinicDto ToDto(MedicalClinic c) => new()
    {
        Id = c.Id, Name = c.Name, Address = c.Address, City = c.City, Phone = c.Phone, IsActive = c.IsActive
    };
}

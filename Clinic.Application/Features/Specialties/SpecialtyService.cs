using System.ComponentModel.DataAnnotations;
using Clinic.Application.Common.Exceptions;
using Clinic.Application.Common.Interfaces;
using Clinic.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Clinic.Application.Features.Specialties;

public class SpecialtyDto
{
    public int Id { get; init; }
    public string NameAr { get; init; } = string.Empty;
    public string NameEn { get; init; } = string.Empty;
}

public class SaveSpecialtyRequest
{
    [Required, StringLength(100, MinimumLength = 2)]
    public string NameAr { get; init; } = string.Empty;

    [Required, StringLength(100, MinimumLength = 2)]
    public string NameEn { get; init; } = string.Empty;
}

public interface ISpecialtyService
{
    Task<IReadOnlyList<SpecialtyDto>> ListAsync(CancellationToken ct);
    Task<SpecialtyDto> GetAsync(int id, CancellationToken ct);
    Task<SpecialtyDto> CreateAsync(SaveSpecialtyRequest request, CancellationToken ct);
    Task<SpecialtyDto> UpdateAsync(int id, SaveSpecialtyRequest request, CancellationToken ct);
    Task DeleteAsync(int id, CancellationToken ct);
}

public class SpecialtyService : ISpecialtyService
{
    private readonly IAppDbContext _db;

    public SpecialtyService(IAppDbContext db)
    {
        _db = db;
    }

    public async Task<IReadOnlyList<SpecialtyDto>> ListAsync(CancellationToken ct)
    {
        return await _db.Specialties.AsNoTracking()
            .OrderBy(s => s.NameEn)
            .Select(s => new SpecialtyDto { Id = s.Id, NameAr = s.NameAr, NameEn = s.NameEn })
            .ToListAsync(ct);
    }

    public async Task<SpecialtyDto> GetAsync(int id, CancellationToken ct)
    {
        var specialty = await _db.Specialties.AsNoTracking().FirstOrDefaultAsync(s => s.Id == id, ct)
            ?? throw new NotFoundException("Specialty", id);
        return ToDto(specialty);
    }

    public async Task<SpecialtyDto> CreateAsync(SaveSpecialtyRequest request, CancellationToken ct)
    {
        var nameAr = request.NameAr.Trim();
        var nameEn = request.NameEn.Trim();
        await EnsureNameIsFreeAsync(nameAr, nameEn, null, ct);

        var specialty = new Specialty { NameAr = nameAr, NameEn = nameEn };
        _db.Specialties.Add(specialty);
        await _db.SaveChangesAsync(ct);
        return ToDto(specialty);
    }

    public async Task<SpecialtyDto> UpdateAsync(int id, SaveSpecialtyRequest request, CancellationToken ct)
    {
        var specialty = await _db.Specialties.FirstOrDefaultAsync(s => s.Id == id, ct)
            ?? throw new NotFoundException("Specialty", id);

        var nameAr = request.NameAr.Trim();
        var nameEn = request.NameEn.Trim();
        await EnsureNameIsFreeAsync(nameAr, nameEn, id, ct);

        specialty.NameAr = nameAr;
        specialty.NameEn = nameEn;
        await _db.SaveChangesAsync(ct);
        return ToDto(specialty);
    }

    public async Task DeleteAsync(int id, CancellationToken ct)
    {
        var specialty = await _db.Specialties.FirstOrDefaultAsync(s => s.Id == id, ct)
            ?? throw new NotFoundException("Specialty", id);

        if (await _db.Doctors.AnyAsync(d => d.SpecialtyId == id, ct))
        {
            throw new ConflictException("This specialty is used by one or more doctors and cannot be deleted.");
        }

        _db.Specialties.Remove(specialty);
        await _db.SaveChangesAsync(ct);
    }

    private async Task EnsureNameIsFreeAsync(string nameAr, string nameEn, int? excludeId, CancellationToken ct)
    {
        var exists = await _db.Specialties.AnyAsync(
            s => (s.NameAr == nameAr || s.NameEn == nameEn) && (excludeId == null || s.Id != excludeId), ct);
        if (exists)
        {
            throw new ConflictException("A specialty with the same Arabic or English name already exists.");
        }
    }

    private static SpecialtyDto ToDto(Specialty s) => new() { Id = s.Id, NameAr = s.NameAr, NameEn = s.NameEn };
}

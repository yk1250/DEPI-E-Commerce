using Clinic.Application.Common.Exceptions;
using Clinic.Application.Common.Interfaces;
using Clinic.Application.Common.Security;
using Clinic.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Clinic.Application.Features.Auth;

public interface IAuthService
{
    Task<RegisterResponse> RegisterPatientAsync(RegisterPatientRequest request, CancellationToken ct);
    Task<LoginResponse> LoginAsync(LoginRequest request, CancellationToken ct);
    Task<UserDto> GetCurrentAsync(CancellationToken ct);
}

public class AuthService : IAuthService
{
    private readonly IAppDbContext _db;
    private readonly IIdentityService _identity;
    private readonly ITokenService _tokens;
    private readonly ICurrentUser _currentUser;

    public AuthService(IAppDbContext db, IIdentityService identity, ITokenService tokens, ICurrentUser currentUser)
    {
        _db = db;
        _identity = identity;
        _tokens = tokens;
        _currentUser = currentUser;
    }

    public async Task<RegisterResponse> RegisterPatientAsync(RegisterPatientRequest request, CancellationToken ct)
    {
        var email = request.Email.Trim().ToLowerInvariant();
        var fullName = request.FullName.Trim();
        var dateOfBirth = request.DateOfBirth!.Value;

        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        if (dateOfBirth > today || dateOfBirth.Year < 1900)
        {
            throw new AppValidationException(new Dictionary<string, string[]>
            {
                ["dateOfBirth"] = ["Date of birth must be between 1900-01-01 and today."]
            });
        }

        // الحساب وملف المريض في Transaction واحدة: لو أي خطوة فشلت، مفيش حاجة بتتحفظ
        await using var tx = await _db.Database.BeginTransactionAsync(ct);

        var userId = await _identity.CreateUserAsync(new NewUser(
            email, request.Password, fullName, request.PhoneNumber,
            Roles.Patient, request.PreferredLanguage, EmailConfirmed: false), ct);

        var patient = new Patient
        {
            UserId = userId,
            DateOfBirth = dateOfBirth.ToDateTime(TimeOnly.MinValue),
            Gender = request.Gender!.Value
        };
        _db.Patients.Add(patient);
        await _db.SaveChangesAsync(ct);

        await tx.CommitAsync(ct);

        return new RegisterResponse
        {
            UserId = userId,
            PatientId = patient.Id,
            Email = email,
            FullName = fullName
        };
    }

    public async Task<LoginResponse> LoginAsync(LoginRequest request, CancellationToken ct)
    {
        var user = await _identity.AuthenticateAsync(request.Email.Trim(), request.Password, ct);
        var (patientId, doctorId, clinicId) = await ResolveProfileIdsAsync(user, ct);
        var token = _tokens.CreateToken(user, patientId, doctorId, clinicId);

        return new LoginResponse
        {
            AccessToken = token.AccessToken,
            ExpiresAtUtc = token.ExpiresAtUtc,
            User = new UserDto
            {
                UserId = user.UserId,
                Email = user.Email,
                FullName = user.FullName,
                Role = user.Role,
                PatientId = patientId,
                DoctorId = doctorId,
                ClinicId = clinicId
            }
        };
    }

    public async Task<UserDto> GetCurrentAsync(CancellationToken ct)
    {
        var userId = _currentUser.UserId ?? throw new UnauthorizedException();

        var summary = await _db.UserSummaries.AsNoTracking()
            .FirstOrDefaultAsync(u => u.Id == userId, ct)
            ?? throw new NotFoundException("User", userId);

        return new UserDto
        {
            UserId = summary.Id,
            Email = summary.Email ?? string.Empty,
            FullName = summary.FullName,
            Role = _currentUser.Role ?? string.Empty,
            PatientId = _currentUser.PatientId,
            DoctorId = _currentUser.DoctorId,
            ClinicId = _currentUser.ClinicId
        };
    }

    private async Task<(int? PatientId, int? DoctorId, int? ClinicId)> ResolveProfileIdsAsync(
        AuthenticatedUser user, CancellationToken ct)
    {
        switch (user.Role)
        {
            case Roles.Patient:
                var patientId = await _db.Patients.AsNoTracking()
                    .Where(p => p.UserId == user.UserId)
                    .Select(p => (int?)p.Id)
                    .FirstOrDefaultAsync(ct);
                return (patientId, null, null);

            case Roles.Doctor:
                var doctor = await _db.Doctors.AsNoTracking()
                    .Where(d => d.UserId == user.UserId)
                    .Select(d => new { d.Id, d.ClinicId })
                    .FirstOrDefaultAsync(ct);
                return (null, doctor?.Id, doctor?.ClinicId);

            case Roles.Receptionist:
                var clinicId = await _db.Receptionists.AsNoTracking()
                    .Where(r => r.UserId == user.UserId)
                    .Select(r => (int?)r.ClinicId)
                    .FirstOrDefaultAsync(ct);
                return (null, null, clinicId);

            default:
                return (null, null, null);
        }
    }
}

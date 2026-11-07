using Clinic.Application.Common.Exceptions;
using Clinic.Application.Common.Interfaces;
using Clinic.Application.Common.Security;
using Clinic.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace Clinic.Application.Features.PatientAccess;

public sealed class PatientAccessGuard : IPatientAccessGuard
{
    private readonly IAppDbContext _db;
    private readonly ICurrentUser _currentUser;

    public PatientAccessGuard(IAppDbContext db, ICurrentUser currentUser)
    {
        _db = db;
        _currentUser = currentUser;
    }

    public async Task<bool> CanAccessAsync(int patientId, CancellationToken ct = default)
    {
        if (!_currentUser.IsAuthenticated)
        {
            return false;
        }

        switch (_currentUser.Role)
        {
            case Roles.Patient:
                // المريض بياناته هو بس
                return _currentUser.PatientId == patientId;

            case Roles.Doctor:
                if (_currentUser.DoctorId is not int doctorId)
                {
                    return false;
                }

                // الدكتور لمريض عنده معاه موعد فعلي (مش ملغي ولا منتهي ولا لسه مستني الدفع)
                return await _db.Appointments.AnyAsync(a =>
                    a.DoctorId == doctorId
                    && a.PatientId == patientId
                    && (a.Status == AppointmentStatus.Confirmed
                        || a.Status == AppointmentStatus.CheckedIn
                        || a.Status == AppointmentStatus.Completed), ct);

            default:
                // الأدمن والريسبشن وأي دور تاني: ممنوع
                return false;
        }
    }

    public async Task EnsureAccessAsync(int patientId, CancellationToken ct = default)
    {
        if (!_currentUser.IsAuthenticated)
        {
            throw new UnauthorizedException();
        }

        if (!await CanAccessAsync(patientId, ct))
        {
            throw new ForbiddenException("You do not have access to this patient's medical data.");
        }
    }
}

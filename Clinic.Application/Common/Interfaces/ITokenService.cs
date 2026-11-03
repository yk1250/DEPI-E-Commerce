namespace Clinic.Application.Common.Interfaces;

public record TokenResult(string AccessToken, DateTime ExpiresAtUtc);

public interface ITokenService
{
    /// بيطلّع JWT فيه: sub, email, name, role, وكمان patient_id / doctor_id / clinic_id لو موجودين.
    TokenResult CreateToken(AuthenticatedUser user, int? patientId, int? doctorId, int? clinicId);
}

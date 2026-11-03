namespace Clinic.Application.Common.Interfaces;

public interface ICurrentUser
{
    bool IsAuthenticated { get; }
    Guid? UserId { get; }      
    string? Role { get; }     
    int? PatientId { get; }   
    int? DoctorId { get; }    
    int? ClinicId { get; }     
    bool IsInRole(string role);
}
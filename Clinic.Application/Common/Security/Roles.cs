namespace Clinic.Application.Common.Security;

public static class Roles
{
    public const string Admin = "Admin";
    public const string Doctor = "Doctor";
    public const string Patient = "Patient";
    public const string Receptionist = "Receptionist";

    public static readonly string[] All = [Admin, Doctor, Patient, Receptionist];
}
using System.ComponentModel.DataAnnotations;

namespace Clinic.Application.Features.Appointments;

public sealed class CancelAppointmentBody
{
    [StringLength(300)]
    public string? Reason { get; init; }
}
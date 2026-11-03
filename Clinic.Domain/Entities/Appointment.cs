using System;
using System.Collections.Generic;
using System.Text;

using Clinic.Domain.Enums;

namespace Clinic.Domain.Entities;

public class Appointment
{
    public int Id { get; set; }
    public int DoctorId { get; set; }
    public int PatientId { get; set; }
    public int? PreviousAppointmentId { get; set; }
    public DateTime StartAt { get; set; }
    public DateTime EndAt { get; set; }
    public AppointmentType Type { get; set; }
    public AppointmentStatus Status { get; set; }

    // السعر المتحسب وقت الحجز (قرار معتمد)
    public decimal PriceSnapshot { get; set; }

    public DateTime? ExpiresAt { get; set; }
    public DateTime? CancelledAt { get; set; }
    public string? CancelReason { get; set; }

    // لمنع الحجز المزدوج، EF هيضبطه كـ rowversion في Infrastructure
    public byte[] RowVersion { get; set; } = [];
    public DateTime CreatedAt { get; set; }
}
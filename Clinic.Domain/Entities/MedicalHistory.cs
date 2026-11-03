using System;
using System.Collections.Generic;
using System.Text;

namespace Clinic.Domain.Entities;

public class MedicalHistory
{
    public int Id { get; set; }
    public int PatientId { get; set; }

    // الحقول دي هتتشفر في Infrastructure
    public string? ChronicConditions { get; set; }
    public string? Allergies { get; set; }
    public string? CurrentMedications { get; set; }

    public DateTime UpdatedAt { get; set; }
}
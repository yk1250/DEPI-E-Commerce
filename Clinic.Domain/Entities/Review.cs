using System;
using System.Collections.Generic;
using System.Text;

namespace Clinic.Domain.Entities;

public class Review
{
    public int Id { get; set; }
    public int AppointmentId { get; set; }
    public int DoctorId { get; set; }
    public int PatientId { get; set; }

    // 1 إلى 5
    public int Rating { get; set; }
    public string? Comment { get; set; }
    public DateTime CreatedAt { get; set; }
}
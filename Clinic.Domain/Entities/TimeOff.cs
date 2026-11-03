using System;
using System.Collections.Generic;
using System.Text;

namespace Clinic.Domain.Entities;

public class TimeOff
{
    public int Id { get; set; }
    public int DoctorId { get; set; }
    public DateTime StartAt { get; set; }
    public DateTime EndAt { get; set; }
    public string? Reason { get; set; }
}
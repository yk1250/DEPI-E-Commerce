using System;
using System.Collections.Generic;
using System.Text;

namespace Clinic.Domain.Entities;

public class WorkingSchedule
{
    public int Id { get; set; }
    public int DoctorId { get; set; }
    public DayOfWeek DayOfWeek { get; set; }
    public TimeOnly StartTime { get; set; }
    public TimeOnly EndTime { get; set; }
    public int SlotDurationMinutes { get; set; }
    public bool IsActive { get; set; } = true;
}

using System;
using System.Collections.Generic;
using System.Text;

namespace Clinic.Domain.Entities;

public class Conversation
{
    public int Id { get; set; }
    public int AppointmentId { get; set; }
    public int DoctorId { get; set; }
    public int PatientId { get; set; }
    public DateTime OpenedAt { get; set; }

    // وقت الكشف المكتمل + 14 يوم، ويتحسب في Application
    public DateTime ExpiresAt { get; set; }
    public bool IsClosed { get; set; }
}
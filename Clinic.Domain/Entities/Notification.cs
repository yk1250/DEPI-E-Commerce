using System;
using System.Collections.Generic;
using System.Text;

using Clinic.Domain.Enums;

namespace Clinic.Domain.Entities;

public class Notification
{
    public long Id { get; set; }
    public Guid UserId { get; set; }
    public int? AppointmentId { get; set; }
    public NotificationType Type { get; set; }
    public NotificationChannel Channel { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Body { get; set; } = string.Empty;
    public DateTime? ScheduledFor { get; set; }
    public DateTime? SentAt { get; set; }
    public DateTime? ReadAt { get; set; }
}
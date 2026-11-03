using System;
using System.Collections.Generic;
using System.Text;

namespace Clinic.Domain.Entities;

public class Message
{
    public long Id { get; set; }
    public int ConversationId { get; set; }
    public Guid SenderUserId { get; set; }
    public int? AttachmentId { get; set; }
    public string Body { get; set; } = string.Empty;
    public DateTime SentAt { get; set; }
    public DateTime? ReadAt { get; set; }
}
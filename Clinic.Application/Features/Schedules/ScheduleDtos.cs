using System.ComponentModel.DataAnnotations;

namespace Clinic.Application.Features.Schedules;

/// فترة عمل أسبوعية. مثال: كل أحد من 09:00:00 لـ 13:00:00 بفترة 30 دقيقة.
/// الأوقات بتوقيت القاهرة. لو الفترة بتنتهي آخر اليوم استخدم 23:59:00.
public class WorkingScheduleItemDto
{
    /// Sunday | Monday | ... | Saturday
    public DayOfWeek DayOfWeek { get; init; }

    /// HH:mm:ss
    public TimeOnly StartTime { get; init; }

    /// HH:mm:ss
    public TimeOnly EndTime { get; init; }

    [Range(5, 240)]
    public int SlotDurationMinutes { get; init; }

    public bool IsActive { get; init; } = true;
}

public class SaveScheduleRequest
{
    /// الجدول الكامل. بيستبدل القديم بالكامل. قايمة فاضية = مسح الجدول.
    [Required, MaxLength(50)]
    public List<WorkingScheduleItemDto> Items { get; init; } = [];
}

public class TimeOffDto
{
    public int Id { get; init; }
    public DateTime StartAt { get; init; }
    public DateTime EndAt { get; init; }
    public string? Reason { get; init; }
}

public class CreateTimeOffRequest
{
    /// بصيغة ISO مع المنطقة الزمنية، مثال: 2026-12-01T00:00:00+02:00 (بيتخزن UTC)
    [Required]
    public DateTimeOffset? StartAt { get; init; }

    [Required]
    public DateTimeOffset? EndAt { get; init; }

    [StringLength(200)]
    public string? Reason { get; init; }
}

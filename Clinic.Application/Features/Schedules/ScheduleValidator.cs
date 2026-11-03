using System.Globalization;
using Clinic.Application.Common.Exceptions;

namespace Clinic.Application.Features.Schedules;

/// قواعد صحة الجدول الأسبوعي (من غير داتابيز، فتتختبر بـ unit tests مباشرة).
public static class ScheduleValidator
{
    public static void Validate(IReadOnlyList<WorkingScheduleItemDto> items)
    {
        var errors = new List<string>();

        for (var i = 0; i < items.Count; i++)
        {
            var item = items[i];
            if (item.EndTime <= item.StartTime)
            {
                errors.Add($"Item {i}: endTime must be after startTime.");
            }
            else if (item.SlotDurationMinutes > (item.EndTime - item.StartTime).TotalMinutes)
            {
                errors.Add($"Item {i}: slotDurationMinutes is longer than the working window.");
            }
        }

        // الفترات الشغالة في نفس اليوم ماينفعش تتقاطع
        var activeValid = items.Where(i => i.IsActive && i.EndTime > i.StartTime).GroupBy(i => i.DayOfWeek);
        foreach (var day in activeValid)
        {
            var ordered = day.OrderBy(i => i.StartTime).ToList();
            for (var k = 1; k < ordered.Count; k++)
            {
                if (ordered[k].StartTime < ordered[k - 1].EndTime)
                {
                    errors.Add(
                        $"{day.Key}: working windows overlap ({Format(ordered[k - 1].StartTime)}-{Format(ordered[k - 1].EndTime)} " +
                        $"and {Format(ordered[k].StartTime)}-{Format(ordered[k].EndTime)}).");
                }
            }
        }

        if (errors.Count > 0)
        {
            throw new AppValidationException(new Dictionary<string, string[]> { ["items"] = errors.ToArray() });
        }
    }

    private static string Format(TimeOnly time) => time.ToString("HH:mm", CultureInfo.InvariantCulture);
}

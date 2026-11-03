using System;
using System.Collections.Generic;
using System.Linq;

namespace Clinic.Application.Features.Slots;

public static class SlotCalculator
{
    public static IReadOnlyList<TimeOnly> Calculate(
        TimeOnly start,
        TimeOnly end,
        int slotMinutes,
        IEnumerable<TimeOnly>? bookedStarts = null)
    {
        var booked = bookedStarts is null
            ? new HashSet<TimeOnly>()
            : bookedStarts.ToHashSet();

        var slots = new List<TimeOnly>();
        var current = start;

        while (current.AddMinutes(slotMinutes) <= end)
        {
            if (!booked.Contains(current))
            {
                slots.Add(current);
            }

            current = current.AddMinutes(slotMinutes);
        }

        return slots;
    }
}
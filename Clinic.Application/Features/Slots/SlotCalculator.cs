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
        IEnumerable<TimeOnly>? bookedStarts = null,
        IEnumerable<(TimeOnly From, TimeOnly To)>? timeOffs = null)
    {
        var booked = bookedStarts is null
            ? new HashSet<TimeOnly>()
            : bookedStarts.ToHashSet();

        var offs = timeOffs is null
            ? new List<(TimeOnly From, TimeOnly To)>()
            : timeOffs.ToList();

        var slots = new List<TimeOnly>();
        var current = start;

        while (current.AddMinutes(slotMinutes) <= end)
        {
            var slotEnd = current.AddMinutes(slotMinutes);

            bool isBooked = booked.Contains(current);
            bool isOffTime = offs.Any(o => current < o.To && slotEnd > o.From);

            if (!isBooked && !isOffTime)
            {
                slots.Add(current);
            }

            current = slotEnd;
        }

        return slots;
    }
}
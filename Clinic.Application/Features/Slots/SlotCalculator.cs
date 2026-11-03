using System;
using System.Collections.Generic;

namespace Clinic.Application.Features.Slots;

public static class SlotCalculator
{
    public static IReadOnlyList<TimeOnly> Calculate(TimeOnly start, TimeOnly end, int slotMinutes)
    {
        var slots = new List<TimeOnly>();
        var current = start;

        while (current.AddMinutes(slotMinutes) <= end)
        {
            slots.Add(current);
            current = current.AddMinutes(slotMinutes);
        }

        return slots;
    }
}
using System;
using System.Collections.Generic;
using Clinic.Application.Features.Slots;
using Xunit;

namespace Clinic.Application.Tests.Slots;

public class SlotCalculatorTests
{
    [Fact]
    public void Doctor_works_9_to_10_with_30_min_slots_gets_2_slots()
    {
        // Arrange
        var start = new TimeOnly(9, 0);
        var end = new TimeOnly(10, 0);
        int slotMinutes = 30;

        // Act
        var slots = SlotCalculator.Calculate(start, end, slotMinutes);

        // Assert
        Assert.Equal(2, slots.Count);
    }

    [Fact]
    public void Doctor_works_9_to_9_50_with_30_min_slots_gets_1_slot()
    {
        // Arrange
        var start = new TimeOnly(9, 0);
        var end = new TimeOnly(9, 50);
        int slotMinutes = 30;

        // Act
        var slots = SlotCalculator.Calculate(start, end, slotMinutes);

        // Assert
        Assert.Single(slots);
        Assert.Equal(new TimeOnly(9, 0), slots[0]);
    }

    [Fact]
    public void Booked_slot_is_excluded_from_result()
    {
        // Arrange
        var start = new TimeOnly(9, 0);
        var end = new TimeOnly(10, 0);
        int slotMinutes = 30;
        var booked = new List<TimeOnly> { new TimeOnly(9, 0) };

        // Act
        var slots = SlotCalculator.Calculate(start, end, slotMinutes, booked);

        // Assert
        Assert.Single(slots);
        Assert.Equal(new TimeOnly(9, 30), slots[0]);
    }
}
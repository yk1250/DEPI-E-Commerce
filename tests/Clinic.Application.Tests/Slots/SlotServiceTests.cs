using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Clinic.Application.Features.Slots;
using Xunit;

namespace Clinic.Application.Tests.Slots;

public class SlotServiceTests
{
    private sealed class FakeAvailabilityReader : IDoctorAvailabilityReader
    {
        private readonly DoctorAvailability? _result;

        public FakeAvailabilityReader(DoctorAvailability? result)
        {
            _result = result;
        }

        public Task<DoctorAvailability?> GetAsync(int doctorId, DateOnly date, CancellationToken ct = default)
        {
            return Task.FromResult(_result);
        }
    }

    [Fact]
    public async Task Returns_free_slots_excluding_booked()
    {
        // Arrange
        var availability = new DoctorAvailability(
            new TimeOnly(9, 0),
            new TimeOnly(10, 0),
            30,
            new List<TimeOnly> { new TimeOnly(9, 0) },
            new List<(TimeOnly From, TimeOnly To)>());

        var service = new SlotService(new FakeAvailabilityReader(availability));

        // Act
        var slots = await service.GetSlotsAsync(1, new DateOnly(2026, 11, 3));

        // Assert
        Assert.Single(slots);
        Assert.Equal(new TimeOnly(9, 30), slots[0]);
    }

    [Fact]
    public async Task Returns_empty_when_doctor_has_no_schedule_that_day()
    {
        // Arrange
        var service = new SlotService(new FakeAvailabilityReader(null));

        // Act
        var slots = await service.GetSlotsAsync(1, new DateOnly(2026, 11, 3));

        // Assert
        Assert.Empty(slots);
    }
}
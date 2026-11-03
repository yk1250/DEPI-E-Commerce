using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Clinic.Application.Features.Slots;

public class SlotService
{
    private readonly IDoctorAvailabilityReader _reader;

    public SlotService(IDoctorAvailabilityReader reader)
    {
        _reader = reader;
    }

    public async Task<IReadOnlyList<TimeOnly>> GetSlotsAsync(
        int doctorId,
        DateOnly date,
        CancellationToken ct = default)
    {
        var availability = await _reader.GetAsync(doctorId, date, ct);

        if (availability is null)
        {
            return Array.Empty<TimeOnly>();
        }

        return SlotCalculator.Calculate(
            availability.WorkStart,
            availability.WorkEnd,
            availability.SlotMinutes,
            availability.BookedStarts,
            availability.TimeOffs);
    }
}
using System;
using System.Collections.Generic;

namespace Clinic.Application.Features.Slots;

public sealed record DoctorAvailability(
    TimeOnly WorkStart,
    TimeOnly WorkEnd,
    int SlotMinutes,
    IReadOnlyList<TimeOnly> BookedStarts,
    IReadOnlyList<(TimeOnly From, TimeOnly To)> TimeOffs);
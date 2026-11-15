using System;

namespace Clinic.Application.Features.Waitlist;

public sealed record WaitlistEntryDto(int Id, int DoctorId, DateOnly DesiredDate, bool IsActive);
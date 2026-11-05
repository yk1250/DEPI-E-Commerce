using System;

namespace Clinic.Application.Features.Waitlist;

public sealed record JoinWaitlistBody(int DoctorId, DateOnly DesiredDate);
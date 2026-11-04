using System;
using Clinic.Domain.Enums;

namespace Clinic.Application.Features.Appointments;

public sealed record AppointmentDto(
    int Id,
    int DoctorId,
    int PatientId,
    DateTime StartAt,
    DateTime EndAt,
    AppointmentStatus Status,
    decimal PriceSnapshot,
    DateTime? ExpiresAt);
using System;
using Clinic.Domain.Enums;

namespace Clinic.Application.Features.Appointments;

public sealed record BookAppointmentBody(
    int DoctorId,
    DateTime StartAtUtc,
    AppointmentType Type,
    PaymentMethod PaymentMethod);
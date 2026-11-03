using System;
using System.Threading;
using System.Threading.Tasks;

namespace Clinic.Application.Features.Slots;

public interface IDoctorAvailabilityReader
{
    // بيرجّع null لو الدكتور مش موجود أو مفيش دوام في اليوم ده
    Task<DoctorAvailability?> GetAsync(int doctorId, DateOnly date, CancellationToken ct = default);
}
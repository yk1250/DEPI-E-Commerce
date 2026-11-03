using Clinic.Application.Common.Interfaces;

namespace Clinic.Tests;

/// وقت ثابت للاختبارات (بدل الوقت الحقيقي).
internal sealed class FixedTimeProvider : TimeProvider
{
    private readonly DateTimeOffset _now;

    public FixedTimeProvider(DateTimeOffset now)
    {
        _now = now;
    }

    public override DateTimeOffset GetUtcNow() => _now;
}

/// مستخدم حالي مزيف: بنحدد الـ ids اللي عايزينها.
internal sealed class FakeCurrentUser : ICurrentUser
{
    public bool IsAuthenticated { get; init; } = true;
    public Guid? UserId { get; init; } = Guid.NewGuid();
    public string? Role { get; init; }
    public int? PatientId { get; init; }
    public int? DoctorId { get; init; }
    public int? ClinicId { get; init; }

    public bool IsInRole(string role) => Role == role;
}

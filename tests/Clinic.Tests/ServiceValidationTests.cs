using Clinic.Application.Common.Exceptions;
using Clinic.Application.Features.Auth;
using Clinic.Application.Features.Doctors;
using Clinic.Application.Features.Schedules;
using Xunit;

namespace Clinic.Tests;

/// اختبارات القواعد اللي بتتحقق قبل ما أي كود يلمس الداتابيز، فبنبعت null للـ DbContext.
/// لو حد غيّر الترتيب وخلّى الـ Service يقرا الداتابيز قبل التحقق، هتفشل هنا (NullReferenceException).
public class ServiceValidationTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 1, 12, 0, 0, TimeSpan.Zero);

    private static DoctorService NewDoctorService() => new(null!, null!, new FakeCurrentUser());

    private static AuthService NewAuthService() => new(null!, null!, null!, new FakeCurrentUser());

    private static ScheduleService NewScheduleService(int? doctorId) =>
        new(null!, new FakeCurrentUser { Role = "Doctor", DoctorId = doctorId }, new FixedTimeProvider(Now));

    // ------------------------------------------------------------ بحث الدكاترة

    [Fact]
    public async Task Search_rejects_min_price_above_max_price()
    {
        var query = new DoctorSearchQuery { MinPrice = 500, MaxPrice = 100 };

        var ex = await Assert.ThrowsAsync<AppValidationException>(() => NewDoctorService().SearchAsync(query, CancellationToken.None));

        Assert.True(ex.Errors.ContainsKey("minPrice"));
    }

    [Fact]
    public async Task Search_rejects_an_unknown_sort_field()
    {
        var query = new DoctorSearchQuery { SortBy = "color" };

        var ex = await Assert.ThrowsAsync<AppValidationException>(() => NewDoctorService().SearchAsync(query, CancellationToken.None));

        Assert.True(ex.Errors.ContainsKey("sortBy"));
    }

    [Fact]
    public async Task Search_rejects_an_unknown_sort_direction()
    {
        var query = new DoctorSearchQuery { SortDir = "up" };

        var ex = await Assert.ThrowsAsync<AppValidationException>(() => NewDoctorService().SearchAsync(query, CancellationToken.None));

        Assert.True(ex.Errors.ContainsKey("sortDir"));
    }

    // ------------------------------------------------------------ تسجيل المريض

    [Fact]
    public async Task Register_rejects_a_birth_date_in_the_future()
    {
        var request = new RegisterPatientRequest
        {
            DateOfBirth = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(1)
        };

        var ex = await Assert.ThrowsAsync<AppValidationException>(() => NewAuthService().RegisterPatientAsync(request, CancellationToken.None));

        Assert.True(ex.Errors.ContainsKey("dateOfBirth"));
    }

    [Fact]
    public async Task Register_rejects_a_birth_date_before_1900()
    {
        var request = new RegisterPatientRequest { DateOfBirth = new DateOnly(1899, 12, 31) };

        var ex = await Assert.ThrowsAsync<AppValidationException>(() => NewAuthService().RegisterPatientAsync(request, CancellationToken.None));

        Assert.True(ex.Errors.ContainsKey("dateOfBirth"));
    }

    // ------------------------------------------------------------ الإجازات

    [Fact]
    public async Task Time_off_ending_before_it_starts_is_rejected()
    {
        var request = new CreateTimeOffRequest
        {
            StartAt = Now.AddDays(5),
            EndAt = Now.AddDays(4)
        };

        var ex = await Assert.ThrowsAsync<AppValidationException>(() => NewScheduleService(doctorId: 1).CreateMyTimeOffAsync(request, CancellationToken.None));

        Assert.Contains("endAt must be after startAt.", ex.Errors["timeOff"]);
    }

    [Fact]
    public async Task Time_off_entirely_in_the_past_is_rejected()
    {
        var request = new CreateTimeOffRequest
        {
            StartAt = Now.AddDays(-2),
            EndAt = Now.AddDays(-1)
        };

        var ex = await Assert.ThrowsAsync<AppValidationException>(() => NewScheduleService(doctorId: 1).CreateMyTimeOffAsync(request, CancellationToken.None));

        Assert.Contains("The time off period must end in the future.", ex.Errors["timeOff"]);
    }

    // ------------------------------------------------------------ جدول الدكتور

    [Fact]
    public async Task Replacing_the_schedule_with_overlapping_windows_is_rejected()
    {
        var request = new SaveScheduleRequest
        {
            Items =
            [
                new WorkingScheduleItemDto { DayOfWeek = DayOfWeek.Sunday, StartTime = new TimeOnly(9, 0), EndTime = new TimeOnly(13, 0), SlotDurationMinutes = 30 },
                new WorkingScheduleItemDto { DayOfWeek = DayOfWeek.Sunday, StartTime = new TimeOnly(12, 0), EndTime = new TimeOnly(15, 0), SlotDurationMinutes = 30 }
            ]
        };

        await Assert.ThrowsAsync<AppValidationException>(() => NewScheduleService(doctorId: 1).ReplaceMyScheduleAsync(request, CancellationToken.None));
    }

    [Fact]
    public async Task Account_without_a_doctor_profile_cannot_edit_a_schedule()
    {
        var request = new SaveScheduleRequest();

        await Assert.ThrowsAsync<ForbiddenException>(() => NewScheduleService(doctorId: null).ReplaceMyScheduleAsync(request, CancellationToken.None));
    }

    [Fact]
    public async Task Account_without_a_doctor_profile_cannot_add_a_time_off()
    {
        var request = new CreateTimeOffRequest { StartAt = Now.AddDays(1), EndAt = Now.AddDays(2) };

        await Assert.ThrowsAsync<ForbiddenException>(() => NewScheduleService(doctorId: null).CreateMyTimeOffAsync(request, CancellationToken.None));
    }
}

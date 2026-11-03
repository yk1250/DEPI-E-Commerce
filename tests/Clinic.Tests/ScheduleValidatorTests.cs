using Clinic.Application.Common.Exceptions;
using Clinic.Application.Features.Schedules;
using Xunit;

namespace Clinic.Tests;

public class ScheduleValidatorTests
{
    private static WorkingScheduleItemDto Item(DayOfWeek day, string start, string end, int slot = 30, bool active = true) => new()
    {
        DayOfWeek = day,
        StartTime = TimeOnly.ParseExact(start, "HH:mm"),
        EndTime = TimeOnly.ParseExact(end, "HH:mm"),
        SlotDurationMinutes = slot,
        IsActive = active
    };

    private static string[] ItemErrors(AppValidationException ex) => ex.Errors["items"];

    [Fact]
    public void Valid_weekly_schedule_passes()
    {
        var items = new[]
        {
            Item(DayOfWeek.Sunday, "09:00", "13:00"),
            Item(DayOfWeek.Tuesday, "16:00", "20:00")
        };

        ScheduleValidator.Validate(items);
    }

    [Fact]
    public void Empty_schedule_is_valid()
    {
        ScheduleValidator.Validate([]);
    }

    [Fact]
    public void Two_separate_windows_on_the_same_day_pass()
    {
        var items = new[]
        {
            Item(DayOfWeek.Sunday, "09:00", "13:00"),
            Item(DayOfWeek.Sunday, "17:00", "21:00")
        };

        ScheduleValidator.Validate(items);
    }

    [Fact]
    public void Windows_that_only_touch_are_not_an_overlap()
    {
        var items = new[]
        {
            Item(DayOfWeek.Monday, "09:00", "12:00"),
            Item(DayOfWeek.Monday, "12:00", "15:00")
        };

        ScheduleValidator.Validate(items);
    }

    [Fact]
    public void Overlapping_windows_on_the_same_day_are_rejected()
    {
        var items = new[]
        {
            Item(DayOfWeek.Sunday, "09:00", "13:00"),
            Item(DayOfWeek.Sunday, "12:00", "15:00")
        };

        var ex = Assert.Throws<AppValidationException>(() => ScheduleValidator.Validate(items));

        var message = Assert.Single(ItemErrors(ex));
        Assert.Contains("overlap", message);
        Assert.Contains("Sunday", message);
    }

    [Fact]
    public void Overlap_is_detected_even_when_the_list_is_not_sorted()
    {
        var items = new[]
        {
            Item(DayOfWeek.Sunday, "12:00", "15:00"),
            Item(DayOfWeek.Sunday, "09:00", "13:00")
        };

        Assert.Throws<AppValidationException>(() => ScheduleValidator.Validate(items));
    }

    [Fact]
    public void Same_hours_on_different_days_are_not_an_overlap()
    {
        var items = new[]
        {
            Item(DayOfWeek.Sunday, "09:00", "13:00"),
            Item(DayOfWeek.Monday, "09:00", "13:00")
        };

        ScheduleValidator.Validate(items);
    }

    [Fact]
    public void Inactive_windows_are_ignored_in_the_overlap_check()
    {
        var items = new[]
        {
            Item(DayOfWeek.Sunday, "09:00", "13:00"),
            Item(DayOfWeek.Sunday, "10:00", "14:00", active: false)
        };

        ScheduleValidator.Validate(items);
    }

    [Fact]
    public void End_before_start_is_rejected()
    {
        var items = new[] { Item(DayOfWeek.Sunday, "13:00", "09:00") };

        var ex = Assert.Throws<AppValidationException>(() => ScheduleValidator.Validate(items));

        Assert.Contains("endTime must be after startTime", Assert.Single(ItemErrors(ex)));
    }

    [Fact]
    public void End_equal_to_start_is_rejected()
    {
        var items = new[] { Item(DayOfWeek.Sunday, "09:00", "09:00") };

        Assert.Throws<AppValidationException>(() => ScheduleValidator.Validate(items));
    }

    [Fact]
    public void Slot_longer_than_the_window_is_rejected()
    {
        var items = new[] { Item(DayOfWeek.Sunday, "10:00", "11:00", slot: 120) };

        var ex = Assert.Throws<AppValidationException>(() => ScheduleValidator.Validate(items));

        Assert.Contains("longer than the working window", Assert.Single(ItemErrors(ex)));
    }

    [Fact]
    public void Slot_exactly_as_long_as_the_window_is_accepted()
    {
        var items = new[] { Item(DayOfWeek.Sunday, "10:00", "10:30", slot: 30) };

        ScheduleValidator.Validate(items);
    }

    [Fact]
    public void All_problems_are_reported_together()
    {
        var items = new[]
        {
            Item(DayOfWeek.Sunday, "13:00", "09:00"),
            Item(DayOfWeek.Monday, "10:00", "11:00", slot: 120)
        };

        var ex = Assert.Throws<AppValidationException>(() => ScheduleValidator.Validate(items));

        Assert.Equal(2, ItemErrors(ex).Length);
    }
}

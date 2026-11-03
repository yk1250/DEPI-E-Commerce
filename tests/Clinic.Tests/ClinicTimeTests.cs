using Clinic.Application.Common.Time;
using Xunit;

namespace Clinic.Tests;

/// لو اختبار الصيف فشل: جهازك عنده بيانات توقيت قديمة (مصر رجّعت التوقيت الصيفي من 2023).
/// حدّث Windows أو tzdata، لأن ده بيأثر على الـ Slots فعلاً.
public class ClinicTimeTests
{
    [Fact]
    public void Winter_cairo_time_is_two_hours_ahead_of_utc()
    {
        var utc = ClinicTime.ToUtc(new DateOnly(2026, 1, 15), new TimeOnly(10, 0));

        Assert.Equal(new DateTime(2026, 1, 15, 8, 0, 0), utc);
        Assert.Equal(DateTimeKind.Utc, utc.Kind);
    }

    [Fact]
    public void Summer_cairo_time_is_three_hours_ahead_of_utc()
    {
        var utc = ClinicTime.ToUtc(new DateOnly(2026, 7, 15), new TimeOnly(10, 0));

        Assert.Equal(new DateTime(2026, 7, 15, 7, 0, 0), utc);
    }

    [Fact]
    public void ToLocal_converts_utc_to_cairo_time()
    {
        var local = ClinicTime.ToLocal(new DateTime(2026, 1, 15, 8, 0, 0));

        Assert.Equal(new DateTime(2026, 1, 15, 10, 0, 0), local);
    }

    [Theory]
    [InlineData(2026, 1, 15, 9, 30)]
    [InlineData(2026, 7, 15, 17, 45)]
    [InlineData(2026, 10, 20, 11, 0)]
    [InlineData(2026, 12, 31, 23, 30)]
    public void Local_to_utc_and_back_returns_the_same_local_time(int year, int month, int day, int hour, int minute)
    {
        var date = new DateOnly(year, month, day);
        var time = new TimeOnly(hour, minute);

        var roundTrip = ClinicTime.ToLocal(ClinicTime.ToUtc(date, time));

        Assert.Equal(date.ToDateTime(time), roundTrip);
    }

    [Fact]
    public void Late_evening_utc_is_already_the_next_day_in_cairo()
    {
        var date = ClinicTime.LocalDateOf(new DateTime(2026, 1, 15, 22, 30, 0));

        Assert.Equal(new DateOnly(2026, 1, 16), date);
    }

    [Fact]
    public void A_local_time_skipped_by_the_dst_change_does_not_throw()
    {
        // 2026-04-24 هو يوم بداية التوقيت الصيفي في مصر: 00:30 مش موجودة
        var utc = ClinicTime.ToUtc(new DateOnly(2026, 4, 24), new TimeOnly(0, 30));

        Assert.Equal(DateTimeKind.Utc, utc.Kind);
    }
}

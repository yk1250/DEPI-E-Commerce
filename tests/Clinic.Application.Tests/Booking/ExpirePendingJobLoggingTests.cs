using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Clinic.Infrastructure.Booking;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Xunit;

namespace Clinic.Application.Tests.Booking;

public class ExpirePendingJobLoggingTests
{
    // Logger بسيط بيسجّل مستويات الرسائل عشان الاختبار يتأكد منها
    private sealed class ListLogger : ILogger<ExpirePendingJob>
    {
        public List<LogLevel> Levels { get; } = new();

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            Levels.Add(logLevel);
        }
    }

    // Factory بيرمي exception عشان نحاكي دورة فاشلة
    private sealed class ThrowingScopeFactory : IServiceScopeFactory
    {
        public IServiceScope CreateScope()
        {
            throw new InvalidOperationException("Simulated failure in the job cycle.");
        }
    }

    [Fact]
    public async Task Failed_cycle_is_logged_as_error_and_returns_false()
    {
        var logger = new ListLogger();
        var job = new ExpirePendingJob(new ThrowingScopeFactory(), logger);

        var succeeded = await job.RunCycleSafelyAsync(default);

        Assert.False(succeeded);
        Assert.Contains(LogLevel.Error, logger.Levels);
    }
}
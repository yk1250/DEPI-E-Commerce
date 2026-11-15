using System;
using System.Threading;
using System.Threading.Tasks;
using Clinic.Application.Features.Appointments;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Clinic.Infrastructure.Booking;

public sealed class ExpirePendingJob
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<ExpirePendingJob> _logger;

    public ExpirePendingJob(IServiceScopeFactory scopeFactory, ILogger<ExpirePendingJob> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    /// دورة واحدة: scope جديد عشان الـ DbContext ما يتشاركش بين الدورات.
    public async Task<int> RunOnceAsync(CancellationToken ct = default)
    {
        using var scope = _scopeFactory.CreateScope();
        var appointments = scope.ServiceProvider.GetRequiredService<AppointmentService>();
        return await appointments.ExpirePendingAsync(ct);
    }

    /// زي RunOnceAsync بس بتسجّل الخطأ بدل ما ترميه. بترجّع true لو الدورة نجحت.
    public async Task<bool> RunCycleSafelyAsync(CancellationToken ct)
    {
        try
        {
            var expired = await RunOnceAsync(ct);

            if (expired > 0)
            {
                _logger.LogInformation("Expired {Count} unpaid online appointment(s).", expired);
            }

            return true;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Expire-pending cycle failed. Will retry on the next tick.");
            return false;
        }
    }

    /// حلقة دورية. الدورة الفاشلة بتتسجل، والحلقة تكمل في الدورة الجاية.
    public async Task RunLoopAsync(TimeSpan interval, CancellationToken ct)
    {
        using var timer = new PeriodicTimer(interval);

        while (!ct.IsCancellationRequested)
        {
            await RunCycleSafelyAsync(ct);

            try
            {
                await timer.WaitForNextTickAsync(ct);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }
}
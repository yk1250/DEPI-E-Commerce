using System;
using System.Threading;
using System.Threading.Tasks;
using Clinic.Application.Features.Appointments;
using Microsoft.Extensions.DependencyInjection;

namespace Clinic.Infrastructure.Booking;

public sealed class ExpirePendingJob
{
    private readonly IServiceScopeFactory _scopeFactory;

    public ExpirePendingJob(IServiceScopeFactory scopeFactory)
    {
        _scopeFactory = scopeFactory;
    }

    /// دورة واحدة: scope جديد عشان الـ DbContext ما يتشاركش بين الدورات.
    public async Task<int> RunOnceAsync(CancellationToken ct = default)
    {
        using var scope = _scopeFactory.CreateScope();
        var appointments = scope.ServiceProvider.GetRequiredService<AppointmentService>();
        return await appointments.ExpirePendingAsync(ct);
    }

    /// حلقة دورية. لو دورة فشلت، الحلقة تكمل في الدورة الجاية.
    public async Task RunLoopAsync(TimeSpan interval, CancellationToken ct)
    {
        using var timer = new PeriodicTimer(interval);

        while (!ct.IsCancellationRequested)
        {
            try
            {
                await RunOnceAsync(ct);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                break;
            }
            catch (Exception)
            {
                // TODO: نضيف ILogger هنا بعد ما نتأكد إن الـ Job شغال. دلوقتي الدورة الفاشلة بتتجاهل.
            }

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
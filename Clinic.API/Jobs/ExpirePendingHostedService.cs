using System;
using System.Threading;
using System.Threading.Tasks;
using Clinic.Infrastructure.Booking;
using Microsoft.Extensions.Hosting;

namespace Clinic.API.Jobs;

public sealed class ExpirePendingHostedService : BackgroundService
{
    private readonly ExpirePendingJob _job;

    public ExpirePendingHostedService(ExpirePendingJob job)
    {
        _job = job;
    }

    protected override Task ExecuteAsync(CancellationToken stoppingToken)
        => _job.RunLoopAsync(TimeSpan.FromMinutes(1), stoppingToken);
}
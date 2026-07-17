using Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Infrastructure.Jobs;

// Implemented and tested but NOT registered in Program.cs.
// Blocked on Deal.OwnerId: the job cannot determine who to notify until a Deal ownership
// concept (Deal.OwnerId or equivalent) is added. See AGENTS.md for the follow-up note.
public sealed class DealRottingNotificationJob : BackgroundService
{
    private static readonly TimeSpan ScanInterval = TimeSpan.FromHours(6);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<DealRottingNotificationJob> _logger;

    public DealRottingNotificationJob(
        IServiceScopeFactory scopeFactory,
        ILogger<DealRottingNotificationJob> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            await ScanAsync(stoppingToken);
            await Task.Delay(ScanInterval, stoppingToken).ConfigureAwait(false);
        }
    }

    public async Task ScanAsync(CancellationToken ct = default)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<CrmDbContext>();

        var pipelines = await db.Pipelines
            .Where(p => p.RottingThresholdDays != null)
            .ToListAsync(ct);

        var now = DateTime.UtcNow;

        foreach (var pipeline in pipelines)
        {
            var thresholdDate = now.AddDays(-pipeline.RottingThresholdDays!.Value);

            var rottingDeals = await db.Deals
                .Where(d => d.PipelineId == pipeline.Id)
                .Where(d => !db.Activities
                    .Any(a => a.DealId == d.Id && a.OccurredAt >= thresholdDate))
                .ToListAsync(ct);

            foreach (var deal in rottingDeals)
            {
                var alreadyNotified = await db.Notifications
                    .AnyAsync(n =>
                        n.RelatedEntityId == deal.Id &&
                        n.Trigger == NotificationTrigger.DealRotting &&
                        n.CreatedAt >= now.AddHours(-24), ct);

                if (alreadyNotified)
                    continue;

                // Deal.OwnerId is not yet present on the Deal entity.
                // Skip notification creation until the ownership concept is added.
                // Remove this guard and fire the notification once Deal.OwnerId exists.
                _logger.LogDebug(
                    "Deal {DealId} in pipeline {PipelineId} is rotting but Deal.OwnerId is not implemented; skipping notification.",
                    deal.Id, pipeline.Id);
            }
        }
    }
}

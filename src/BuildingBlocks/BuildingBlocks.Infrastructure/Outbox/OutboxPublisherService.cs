using BuildingBlocks.Infrastructure.Persistence;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace BuildingBlocks.Infrastructure.Outbox;

/// <summary>
/// Runs <see cref="OutboxProcessor{TContext}"/> whenever a commit signals new messages, and at least
/// every polling interval. Run exactly one instance per outbox: with several application instances,
/// guard the run with a distributed lock, or publication order within a group is lost.
/// </summary>
public sealed class OutboxPublisherService<TContext>(
    IServiceScopeFactory scopes,
    OutboxSignal signal,
    IOptions<OutboxOptions> options,
    ILogger<OutboxPublisherService<TContext>> logger) : BackgroundService
    where TContext : DbContextBase
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                var processor = scope.ServiceProvider.GetRequiredService<OutboxProcessor<TContext>>();

                OutboxRun run;
                do
                {
                    run = await processor.ProcessAsync(stoppingToken);
                }
                while (run.Read == options.Value.BatchSize && run.Published > 0 && !stoppingToken.IsCancellationRequested);

                await processor.DeletePublishedAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception exception)
            {
                // Keep the publisher alive: the next run retries.
                logger.LogError(exception, "Outbox publication run for {Context} failed", typeof(TContext).Name);
            }

            await signal.WaitAsync(typeof(TContext), options.Value.PollingInterval, stoppingToken);
        }
    }
}

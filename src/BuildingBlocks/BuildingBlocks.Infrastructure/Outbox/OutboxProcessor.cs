using BuildingBlocks.Infrastructure.Messaging;
using BuildingBlocks.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace BuildingBlocks.Infrastructure.Outbox;

/// <summary>Outcome of one publication run.</summary>
public sealed record OutboxRun(int Read, int Published, int Failed);

/// <summary>
/// Publishes the unpublished messages of one bounded context's outbox.
/// <list type="bullet">
/// <item>Messages are read in the order they were stored.</item>
/// <item>Within a message group, order is preserved: a failed or backed-off message holds back the rest of its group.</item>
/// <item>A message without a group never blocks anything.</item>
/// <item>A failed message is retried with exponential backoff.</item>
/// </list>
/// Delivery is at least once: a crash between publishing and recording it republishes the message.
/// Consumers skip duplicates with the inbox.
/// </summary>
public sealed class OutboxProcessor<TContext>(
    TContext db,
    IMessageBus bus,
    TimeProvider timeProvider,
    IOptions<OutboxOptions> options,
    ILogger<OutboxProcessor<TContext>> logger)
    where TContext : DbContextBase
{
    private readonly OutboxOptions _options = options.Value;

    public async Task<OutboxRun> ProcessAsync(CancellationToken cancellationToken)
    {
        var batch = await db.Set<OutboxMessage>()
            .Where(m => m.PublishedAt == null)
            .OrderBy(m => m.Id)
            .Take(_options.BatchSize)
            .ToListAsync(cancellationToken);

        var now = timeProvider.GetUtcNow();
        int published = 0, failed = 0;

        // GroupBy keeps the original order inside each group.
        foreach (var group in batch.GroupBy(m => m.MessageGroup))
        {
            foreach (var message in group)
            {
                if (message.NextAttemptAt > now)
                {
                    if (group.Key is null)
                    {
                        continue;
                    }

                    break;
                }

                try
                {
                    await bus.PublishAsync(ToEnvelope(message), cancellationToken);
                    message.MarkPublished(timeProvider.GetUtcNow());
                    published++;
                    await db.SaveChangesAsync(cancellationToken);
                }
                catch (Exception exception) when (exception is not OperationCanceledException)
                {
                    message.MarkFailed(exception.Message, now + RetryDelay(message.FailedAttempts + 1));
                    failed++;
                    await db.SaveChangesAsync(cancellationToken);
                    logger.LogWarning(exception, "Publishing outbox message {MessageId} ({Type}) failed, attempt {Attempt}; next attempt at {NextAttemptAt}",
                        message.MessageId, message.Type, message.FailedAttempts, message.NextAttemptAt);

                    if (group.Key is not null)
                    {
                        break;
                    }
                }
            }
        }

        return new OutboxRun(batch.Count, published, failed);
    }

    /// <summary>Deletes published messages older than the retention period, so the table does not grow forever.</summary>
    public Task<int> DeletePublishedAsync(CancellationToken cancellationToken)
    {
        var cutoff = timeProvider.GetUtcNow() - _options.Retention;
        return db.Set<OutboxMessage>()
            .Where(m => m.PublishedAt != null && m.PublishedAt < cutoff)
            .ExecuteDeleteAsync(cancellationToken);
    }

    private TimeSpan RetryDelay(int attempt)
    {
        var delay = _options.RetryBaseDelay * Math.Pow(2, Math.Min(attempt - 1, 30));
        return delay < _options.RetryMaxDelay ? delay : _options.RetryMaxDelay;
    }

    private static MessageEnvelope ToEnvelope(OutboxMessage message) =>
        new(message.MessageId, message.Type, message.Payload, message.MessageGroup, message.OccurredAt);
}

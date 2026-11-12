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
/// <item>The fetch skips messages that are backing off and messages queued behind a backed-off message of
/// their group, so a batch full of failures can never starve the healthy messages behind it.</item>
/// <item>Within a message group, order is preserved: a failed message holds back the rest of its group.</item>
/// <item>A message without a group never blocks anything.</item>
/// <item>A failed message is retried with exponential backoff. A message this code cannot read is
/// retried too (a deployment may fix it) but is reported as unresolvable, which deserves an alert.</item>
/// <item>When a message is published but recording it fails, the run stops with
/// <see cref="OutboxPublicationNotRecordedException"/>.</item>
/// </list>
/// Delivery is at least once: a crash between publishing and recording it republishes the message.
/// Consumers skip duplicates with the inbox.
/// </summary>
public sealed class OutboxProcessor<TContext>(
    TContext db,
    IMessageBus bus,
    IntegrationEventTypes types,
    OutboxMetrics metrics,
    TimeProvider timeProvider,
    IOptions<OutboxOptions> options,
    ILogger<OutboxProcessor<TContext>> logger)
    where TContext : DbContextBase
{
    private static readonly string Context = typeof(TContext).Name;

    private readonly OutboxOptions _options = options.Value;

    public async Task<OutboxRun> ProcessAsync(CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();
        var messages = db.Set<OutboxMessage>();

        // On SQL Server, write this as a stored query that first collects the blocked groups into a
        // table variable: a CTE is re-evaluated per candidate row and makes the fetch quadratic.
        var batch = await messages
            .Where(m => m.PublishedAt == null && (m.NextAttemptAt == null || m.NextAttemptAt <= now))
            .Where(m => m.MessageGroup == null || !messages.Any(blocking =>
                blocking.MessageGroup == m.MessageGroup
                && blocking.PublishedAt == null
                && blocking.NextAttemptAt > now
                && blocking.Id < m.Id))
            .OrderBy(m => m.Id)
            .Take(_options.BatchSize)
            .ToListAsync(cancellationToken);

        int published = 0, failed = 0;

        // GroupBy keeps the original order inside each group.
        foreach (var group in batch.GroupBy(m => m.MessageGroup))
        {
            foreach (var message in group)
            {
                if (await TryPublishAsync(message, now, cancellationToken))
                {
                    published++;
                    continue;
                }

                failed++;
                if (group.Key is not null)
                {
                    break;
                }
            }
        }

        return new OutboxRun(batch.Count, published, failed);
    }

    /// <summary>How many messages wait to be published, and since when. Feeds the backlog gauges.</summary>
    public async Task<OutboxPendingStats> GetPendingStatsAsync(CancellationToken cancellationToken)
    {
        var pending = db.Set<OutboxMessage>().Where(m => m.PublishedAt == null);
        var count = await pending.CountAsync(cancellationToken);
        var oldest = await pending
            .OrderBy(m => m.Id)
            .Select(m => (DateTimeOffset?)m.EnqueuedAt)
            .FirstOrDefaultAsync(cancellationToken);
        return new OutboxPendingStats(count, oldest);
    }

    /// <summary>Deletes published messages older than the retention period, so the table does not grow forever.</summary>
    public Task<int> DeletePublishedAsync(CancellationToken cancellationToken)
    {
        var cutoff = timeProvider.GetUtcNow() - _options.Retention;
        return db.Set<OutboxMessage>()
            .Where(m => m.PublishedAt != null && m.PublishedAt < cutoff)
            .ExecuteDeleteAsync(cancellationToken);
    }

    private async Task<bool> TryPublishAsync(OutboxMessage message, DateTimeOffset now, CancellationToken cancellationToken)
    {
        try
        {
            if (!types.IsRegistered(message.Type))
            {
                throw new UnresolvableMessageException($"No integration event is registered under the name '{message.Type}'.");
            }

            await bus.PublishAsync(ToEnvelope(message), cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            await RecordFailureAsync(message, exception, now, cancellationToken);
            return false;
        }

        var publishedAt = timeProvider.GetUtcNow();
        message.MarkPublished(publishedAt);
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            metrics.RecordFailed(Context, message.Type, OutboxFailureReason.NotRecorded);
            throw new OutboxPublicationNotRecordedException(message.MessageId, exception);
        }

        metrics.RecordPublished(Context, message.Type, publishedAt - message.EnqueuedAt);
        return true;
    }

    private async Task RecordFailureAsync(OutboxMessage message, Exception exception, DateTimeOffset now, CancellationToken cancellationToken)
    {
        message.MarkFailed(exception.Message, now + RetryDelay(message.FailedAttempts + 1));
        await db.SaveChangesAsync(cancellationToken);

        if (exception is UnresolvableMessageException)
        {
            metrics.RecordFailed(Context, message.Type, OutboxFailureReason.Unresolvable);
            logger.LogError(exception, "Outbox message {MessageId} ({Type}) cannot be resolved by this version of the code, attempt {Attempt}; it holds back its group until a deployment or a data fix resolves it",
                message.MessageId, message.Type, message.FailedAttempts);
            return;
        }

        metrics.RecordFailed(Context, message.Type, OutboxFailureReason.Transient);
        logger.LogWarning(exception, "Publishing outbox message {MessageId} ({Type}) failed, attempt {Attempt}; next attempt at {NextAttemptAt}",
            message.MessageId, message.Type, message.FailedAttempts, message.NextAttemptAt);
    }

    private TimeSpan RetryDelay(int attempt)
    {
        var delay = _options.RetryBaseDelay * Math.Pow(2, Math.Min(attempt - 1, 30));
        return delay < _options.RetryMaxDelay ? delay : _options.RetryMaxDelay;
    }

    private static MessageEnvelope ToEnvelope(OutboxMessage message) =>
        new(message.MessageId, message.Type, message.Payload, message.MessageGroup, message.OccurredAt);
}

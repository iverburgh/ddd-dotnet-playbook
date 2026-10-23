namespace BuildingBlocks.Infrastructure.Outbox;

/// <summary>An integration event waiting to be published, stored in the same database and transaction as the change that caused it.</summary>
public sealed class OutboxMessage
{
    private OutboxMessage()
    {
        Type = string.Empty;
        Payload = string.Empty;
    }

    /// <summary>Database sequence; publication order within a message group follows it.</summary>
    public long Id { get; private set; }

    /// <summary>Identifies the message to consumers, which use it to skip duplicates.</summary>
    public Guid MessageId { get; private set; }

    public string Type { get; private set; }

    public string Payload { get; private set; }

    public string? MessageGroup { get; private set; }

    public DateTimeOffset OccurredAt { get; private set; }

    public DateTimeOffset EnqueuedAt { get; private set; }

    public DateTimeOffset? PublishedAt { get; private set; }

    public int FailedAttempts { get; private set; }

    public DateTimeOffset? NextAttemptAt { get; private set; }

    public string? LastError { get; private set; }

    internal static OutboxMessage Create(string type, string payload, string? messageGroup, DateTimeOffset occurredAt, DateTimeOffset enqueuedAt) => new()
    {
        MessageId = Guid.NewGuid(),
        Type = type,
        Payload = payload,
        MessageGroup = messageGroup,
        OccurredAt = occurredAt,
        EnqueuedAt = enqueuedAt,
    };

    internal void MarkPublished(DateTimeOffset publishedAt)
    {
        PublishedAt = publishedAt;
        NextAttemptAt = null;
        LastError = null;
    }

    internal void MarkFailed(string error, DateTimeOffset nextAttemptAt)
    {
        FailedAttempts++;
        NextAttemptAt = nextAttemptAt;
        LastError = error.Length <= 2000 ? error : error[..2000];
    }
}

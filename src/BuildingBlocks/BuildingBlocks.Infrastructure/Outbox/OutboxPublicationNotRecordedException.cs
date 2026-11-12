namespace BuildingBlocks.Infrastructure.Outbox;

/// <summary>
/// A message reached the broker, but marking it as published failed. The run stops: whatever broke
/// the save (usually the database) would break the next one too, and every further message would be
/// published again on the next run. The message stays unpublished and is republished later, which
/// consumers absorb through their inbox.
/// </summary>
public sealed class OutboxPublicationNotRecordedException : Exception
{
    public OutboxPublicationNotRecordedException(Guid messageId, Exception innerException)
        : base($"Outbox message {messageId} was published, but recording it failed; it will be published again.", innerException) =>
        MessageId = messageId;

    public Guid MessageId { get; }
}

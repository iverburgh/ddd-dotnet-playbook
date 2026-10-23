namespace BuildingBlocks.Infrastructure.Inbox;

/// <summary>Records that one handler has processed one message, so a redelivered message is skipped.</summary>
public sealed class InboxMessage
{
    private InboxMessage()
    {
        Handler = string.Empty;
    }

    internal InboxMessage(Guid messageId, string handler, DateTimeOffset processedAt)
    {
        MessageId = messageId;
        Handler = handler;
        ProcessedAt = processedAt;
    }

    public Guid MessageId { get; private set; }

    public string Handler { get; private set; }

    public DateTimeOffset ProcessedAt { get; private set; }
}

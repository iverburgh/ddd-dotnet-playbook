namespace BuildingBlocks.Infrastructure.Outbox;

public sealed class OutboxOptions
{
    /// <summary>Maximum number of messages read per run.</summary>
    public int BatchSize { get; set; } = 100;

    /// <summary>How long the publisher sleeps when nothing signals new messages; the safety net for missed signals and retries.</summary>
    public TimeSpan PollingInterval { get; set; } = TimeSpan.FromSeconds(5);

    /// <summary>Delay before the first retry; it doubles with every further failure.</summary>
    public TimeSpan RetryBaseDelay { get; set; } = TimeSpan.FromSeconds(2);

    public TimeSpan RetryMaxDelay { get; set; } = TimeSpan.FromMinutes(5);

    /// <summary>How long published messages are kept before they are deleted.</summary>
    public TimeSpan Retention { get; set; } = TimeSpan.FromDays(7);
}

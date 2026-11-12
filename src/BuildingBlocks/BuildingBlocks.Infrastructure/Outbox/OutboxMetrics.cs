using System.Collections.Concurrent;
using System.Diagnostics.Metrics;

namespace BuildingBlocks.Infrastructure.Outbox;

/// <summary>Why a message was not published.</summary>
public static class OutboxFailureReason
{
    /// <summary>The broker or the network failed; a retry will probably succeed.</summary>
    public const string Transient = "transient";

    /// <summary>This version of the code cannot read the message; only a code or data change helps.</summary>
    public const string Unresolvable = "unresolvable";

    /// <summary>The message was published, but recording that failed; it will be published again.</summary>
    public const string NotRecorded = "not_recorded";
}

/// <summary>Unpublished messages of one outbox at the moment they were counted.</summary>
public sealed record OutboxPendingStats(int Count, DateTimeOffset? OldestEnqueuedAt);

/// <summary>
/// The numbers needed to operate an outbox, published with <see cref="System.Diagnostics.Metrics"/>
/// so any OpenTelemetry exporter can pick them up (subscribe to <see cref="MeterName"/>).
/// <list type="bullet">
/// <item><c>outbox.messages.published</c> and <c>outbox.messages.failed</c> (tagged with the context and the failure reason).</item>
/// <item><c>outbox.publish.latency</c>: time from enqueue to publication, per event type.</item>
/// <item><c>outbox.pending.count</c> and <c>outbox.pending.oldest_age</c>: the backlog, per context. The age is
/// computed when it is observed, so it keeps growing when the publisher stops.</item>
/// </list>
/// </summary>
public sealed class OutboxMetrics : IDisposable
{
    public const string MeterName = "BuildingBlocks.Outbox";

    private readonly TimeProvider _timeProvider;
    private readonly ConcurrentDictionary<string, OutboxPendingStats> _pending = new(StringComparer.Ordinal);
    private readonly Counter<long> _published;
    private readonly Counter<long> _failed;
    private readonly Histogram<double> _latency;

    public OutboxMetrics(TimeProvider timeProvider)
    {
        _timeProvider = timeProvider;
        Meter = new Meter(MeterName);
        _published = Meter.CreateCounter<long>("outbox.messages.published", "{message}", "Messages published from the outbox.");
        _failed = Meter.CreateCounter<long>("outbox.messages.failed", "{message}", "Failed publication attempts, by reason.");
        _latency = Meter.CreateHistogram<double>("outbox.publish.latency", "s", "Time from enqueueing a message to publishing it.");
        Meter.CreateObservableGauge("outbox.pending.count", ObservePendingCount, "{message}", "Messages waiting to be published.");
        Meter.CreateObservableGauge("outbox.pending.oldest_age", ObserveOldestAge, "s", "Age of the oldest message waiting to be published.");
    }

    public Meter Meter { get; }

    public void Dispose() => Meter.Dispose();

    internal void RecordPublished(string context, string type, TimeSpan latency)
    {
        _published.Add(1, new KeyValuePair<string, object?>("context", context));
        _latency.Record(latency.TotalSeconds, new KeyValuePair<string, object?>("context", context), new KeyValuePair<string, object?>("type", type));
    }

    internal void RecordFailed(string context, string type, string reason) =>
        _failed.Add(
            1,
            new KeyValuePair<string, object?>("context", context),
            new KeyValuePair<string, object?>("type", type),
            new KeyValuePair<string, object?>("reason", reason));

    internal void RecordPending(string context, OutboxPendingStats stats) => _pending[context] = stats;

    private IEnumerable<Measurement<long>> ObservePendingCount() =>
        _pending.Select(p => new Measurement<long>(p.Value.Count, new KeyValuePair<string, object?>("context", p.Key)));

    private IEnumerable<Measurement<double>> ObserveOldestAge()
    {
        var now = _timeProvider.GetUtcNow();
        return _pending.Select(p => new Measurement<double>(
            p.Value.OldestEnqueuedAt is { } oldest ? Math.Max(0, (now - oldest).TotalSeconds) : 0,
            new KeyValuePair<string, object?>("context", p.Key)));
    }
}

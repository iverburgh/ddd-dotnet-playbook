using System.Diagnostics.Metrics;
using BuildingBlocks.Infrastructure.Inbox;
using BuildingBlocks.Infrastructure.Messaging;
using BuildingBlocks.Infrastructure.Outbox;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace BuildingBlocks.Infrastructure.Tests;

public sealed class OutboxProcessorTests : InfrastructureTestBase
{
    private Task<OutboxRun> ProcessAsync() =>
        InScope(sp => sp.GetRequiredService<OutboxProcessor<TestDbContext>>().ProcessAsync(CancellationToken.None));

    private Task SqlAsync(string sql) =>
        InScope(sp => sp.GetRequiredService<TestDbContext>().Database.ExecuteSqlRawAsync(sql));

    private Guid WidgetId(MessageEnvelope envelope) =>
        ((WidgetCreatedIntegrationEvent)Services.GetRequiredService<IntegrationEventTypes>().Deserialize(envelope)).WidgetId;

    private List<Guid> PublishedWidgetIds() => [.. Bus.Published.Select(WidgetId)];

    [Test]
    public async Task Messages_of_a_group_are_published_in_the_order_they_were_stored()
    {
        var ids = await EnqueueAsync("order-1", "order-2", null, "order-1", "order-2");

        var run = await ProcessAsync();

        Assert.That(run, Is.EqualTo(new OutboxRun(Read: 5, Published: 5, Failed: 0)));
        Assert.That(await OutboxMessagesAsync(), Has.All.Matches<OutboxMessage>(m => m.PublishedAt == Start));
        var published = PublishedWidgetIds();
        Assert.That(published.Where(id => id == ids[0] || id == ids[3]), Is.EqualTo(new[] { ids[0], ids[3] }));
        Assert.That(published.Where(id => id == ids[1] || id == ids[4]), Is.EqualTo(new[] { ids[1], ids[4] }));
    }

    [Test]
    public async Task A_failure_holds_back_its_own_group_only()
    {
        var ids = await EnqueueAsync("group-a", "group-a", "group-b", null, null);
        var failing = new HashSet<Guid> { ids[0], ids[3] };
        Bus.FailWhen = e => failing.Contains(WidgetId(e));

        var run = await ProcessAsync();

        Assert.That(run, Is.EqualTo(new OutboxRun(Read: 5, Published: 2, Failed: 2)));
        Assert.That(PublishedWidgetIds(), Is.EqualTo(new[] { ids[2], ids[4] }),
            "group-b and the ungrouped message go ahead; the second message of group-a waits for the first.");
        var failed = (await OutboxMessagesAsync())[0];
        Assert.That(failed.FailedAttempts, Is.EqualTo(1));
        Assert.That(failed.LastError, Is.EqualTo("Broker unavailable"));
    }

    [Test]
    public async Task A_failed_message_is_retried_with_exponential_backoff()
    {
        await EnqueueAsync("group-a");
        Bus.FailWhen = _ => true;

        var expectedDelays = new[] { 2, 4, 8, 16, 30, 30 }; // base 2s, doubling, capped at 30s
        foreach (var delay in expectedDelays)
        {
            var attemptAt = Time.GetUtcNow();
            Assert.That((await ProcessAsync()).Failed, Is.EqualTo(1));
            Assert.That((await OutboxMessagesAsync())[0].NextAttemptAt, Is.EqualTo(attemptAt.AddSeconds(delay)));

            Time.Advance(TimeSpan.FromSeconds(delay) - TimeSpan.FromMilliseconds(1));
            Assert.That((await ProcessAsync()).Failed, Is.Zero, "No retry before the backoff has passed.");
            Time.Advance(TimeSpan.FromMilliseconds(1));
        }

        Bus.FailWhen = _ => false;
        Assert.That((await ProcessAsync()).Published, Is.EqualTo(1));
        Assert.That((await OutboxMessagesAsync())[0].FailedAttempts, Is.EqualTo(expectedDelays.Length));
    }

    [Test]
    public async Task A_backed_off_message_keeps_the_rest_of_its_group_waiting()
    {
        var ids = await EnqueueAsync("group-a", "group-a");
        Bus.FailWhen = e => WidgetId(e) == ids[0];
        await ProcessAsync();
        Bus.FailWhen = _ => false;

        Assert.That((await ProcessAsync()).Published, Is.Zero);

        Time.Advance(TimeSpan.FromSeconds(2));
        Assert.That((await ProcessAsync()).Published, Is.EqualTo(2));
        Assert.That(PublishedWidgetIds(), Is.EqualTo(ids));
    }

    [Test]
    public async Task Messages_in_backoff_never_starve_the_messages_behind_them()
    {
        Services.GetRequiredService<IOptions<OutboxOptions>>().Value.BatchSize = 2;
        var ids = await EnqueueAsync(null, null, "group-a", "group-a", null);
        var failing = new HashSet<Guid> { ids[0], ids[1], ids[2] };
        Bus.FailWhen = e => failing.Contains(WidgetId(e));

        Assert.That(await ProcessAsync(), Is.EqualTo(new OutboxRun(Read: 2, Published: 0, Failed: 2)));
        Assert.That(await ProcessAsync(), Is.EqualTo(new OutboxRun(Read: 2, Published: 0, Failed: 1)),
            "The next run skips the two backed-off messages and reaches group-a; its head fails and holds back the rest.");
        Assert.That(await ProcessAsync(), Is.EqualTo(new OutboxRun(Read: 1, Published: 1, Failed: 0)),
            "Without any time passing, the healthy message behind a full batch of failures still goes out.");
        Assert.That(PublishedWidgetIds(), Is.EqualTo(new[] { ids[4] }));
    }

    [Test]
    public async Task A_message_this_code_cannot_read_is_reported_as_unresolvable_and_holds_back_its_group()
    {
        using var failures = new FailureReasons(Services.GetRequiredService<OutboxMetrics>());
        var ids = await EnqueueAsync("group-a", "group-a");
        await SqlAsync("UPDATE TestOutboxMessages SET Type = 'test.widget-renamed.v1' WHERE Id = (SELECT MIN(Id) FROM TestOutboxMessages)");

        Assert.That(await ProcessAsync(), Is.EqualTo(new OutboxRun(Read: 2, Published: 0, Failed: 1)));
        Assert.That(Bus.Published, Is.Empty, "An unknown contract never reaches the broker.");
        Assert.That(failures.Reasons, Is.EqualTo(new[] { OutboxFailureReason.Unresolvable }));
        var message = (await OutboxMessagesAsync())[0];
        Assert.That(message.FailedAttempts, Is.EqualTo(1));
        Assert.That(message.LastError, Does.Contain("test.widget-renamed.v1"));

        // A deployment that knows the contract again, or a data fix, releases the group.
        await SqlAsync("UPDATE TestOutboxMessages SET Type = 'test.widget-created.v1'");
        Time.Advance(TimeSpan.FromSeconds(2));
        Assert.That((await ProcessAsync()).Published, Is.EqualTo(2));
        Assert.That(PublishedWidgetIds(), Is.EqualTo(ids));
    }

    [Test]
    public async Task A_message_that_was_published_but_not_recorded_stops_the_run_and_is_published_again()
    {
        using var failures = new FailureReasons(Services.GetRequiredService<OutboxMetrics>());
        var ids = await EnqueueAsync(null, null);
        Bus.OnPublished = _ => SqlAsync(
            "CREATE TRIGGER database_unavailable BEFORE UPDATE ON TestOutboxMessages BEGIN SELECT RAISE(ABORT, 'database unavailable'); END;");

        var exception = Assert.ThrowsAsync<OutboxPublicationNotRecordedException>(ProcessAsync);

        Assert.That(exception!.InnerException, Is.InstanceOf<DbUpdateException>());
        Assert.That(PublishedWidgetIds(), Is.EqualTo(new[] { ids[0] }), "The run stops instead of publishing more messages it cannot record.");
        Assert.That(failures.Reasons, Is.EqualTo(new[] { OutboxFailureReason.NotRecorded }));

        Bus.OnPublished = _ => Task.CompletedTask;
        await SqlAsync("DROP TRIGGER database_unavailable");
        Assert.That(await OutboxMessagesAsync(), Has.All.Matches<OutboxMessage>(m => m.PublishedAt == null && m.FailedAttempts == 0),
            "Nothing went wrong with the message itself, so it is not marked as failed.");

        Assert.That((await ProcessAsync()).Published, Is.EqualTo(2));
        Assert.That(PublishedWidgetIds(), Is.EqualTo(new[] { ids[0], ids[0], ids[1] }), "At least once: the inbox absorbs the duplicate.");
    }

    [Test]
    public async Task The_backlog_gauges_report_the_pending_count_and_the_age_of_the_oldest_message()
    {
        await EnqueueAsync((string?)null);
        await ProcessAsync();
        Time.Advance(TimeSpan.FromMinutes(1));
        await EnqueueAsync(null, null);

        var stats = await InScope(sp => sp.GetRequiredService<OutboxProcessor<TestDbContext>>().GetPendingStatsAsync(CancellationToken.None));
        Assert.That(stats, Is.EqualTo(new OutboxPendingStats(2, Start.AddMinutes(1))));

        var metrics = Services.GetRequiredService<OutboxMetrics>();
        metrics.RecordPending(nameof(TestDbContext), stats);
        Time.Advance(TimeSpan.FromMinutes(20));

        var gauges = ObserveGauges(metrics);
        Assert.That(gauges["outbox.pending.count"], Is.EqualTo(2));
        Assert.That(gauges["outbox.pending.oldest_age"], Is.EqualTo(TimeSpan.FromMinutes(20).TotalSeconds),
            "The age is computed when observed, so it keeps growing while nothing gets published.");
    }

    private static Dictionary<string, double> ObserveGauges(OutboxMetrics metrics)
    {
        var values = new Dictionary<string, double>();
        using var listener = new MeterListener();
        listener.InstrumentPublished = (instrument, l) =>
        {
            if (ReferenceEquals(instrument.Meter, metrics.Meter))
            {
                l.EnableMeasurementEvents(instrument);
            }
        };
        listener.SetMeasurementEventCallback<long>((instrument, value, _, _) => values[instrument.Name] = value);
        listener.SetMeasurementEventCallback<double>((instrument, value, _, _) => values[instrument.Name] = value);
        listener.Start();
        listener.RecordObservableInstruments();
        return values;
    }

    [Test]
    public async Task Published_messages_are_deleted_after_the_retention_period()
    {
        await EnqueueAsync(null, null);
        await ProcessAsync();
        await EnqueueAsync((string?)null);

        Time.Advance(TimeSpan.FromDays(7));
        Assert.That(await InScope(sp => sp.GetRequiredService<OutboxProcessor<TestDbContext>>().DeletePublishedAsync(CancellationToken.None)), Is.Zero);

        Time.Advance(TimeSpan.FromSeconds(1));
        Assert.That(await InScope(sp => sp.GetRequiredService<OutboxProcessor<TestDbContext>>().DeletePublishedAsync(CancellationToken.None)), Is.EqualTo(2));
        Assert.That(await OutboxMessagesAsync(), Has.Count.EqualTo(1), "Unpublished messages are never deleted.");
    }

    [Test]
    public async Task Each_context_has_its_own_wake_up_signal()
    {
        var signal = Services.GetRequiredService<OutboxSignal>();
        using var cancel = new CancellationTokenSource();

        signal.Notify(typeof(TestDbContext));
        var otherContext = signal.WaitAsync(typeof(object), TimeSpan.FromMinutes(1), cancel.Token);
        var ownContext = signal.WaitAsync(typeof(TestDbContext), TimeSpan.FromMinutes(1), CancellationToken.None);

        Assert.That(await Task.WhenAny(ownContext, Task.Delay(TimeSpan.FromSeconds(5))), Is.SameAs(ownContext));
        Assert.That(otherContext.IsCompleted, Is.False);
        await cancel.CancelAsync();
        Assert.CatchAsync<OperationCanceledException>(() => otherContext);
    }
}

public sealed class InboxTests : InfrastructureTestBase
{
    private InMemoryMessageBus CreateBus() => new(
        Services,
        Services.GetRequiredService<IntegrationEventTypes>(),
        Services.GetServices<IIntegrationEventSubscription>());

    private static MessageEnvelope Envelope() => new(
        Guid.NewGuid(),
        "test.widget-created.v1",
        $$"""{"widgetId":"{{Guid.NewGuid()}}","occurredAt":"2025-01-01T12:00:00+00:00"}""",
        null,
        Start);

    private Task<int> InboxCountAsync() => InScope(sp => sp.GetRequiredService<TestDbContext>().Set<InboxMessage>().CountAsync());

    [Test]
    public async Task A_redelivered_message_is_handled_once()
    {
        var bus = CreateBus();
        var envelope = Envelope();

        await bus.PublishAsync(envelope, CancellationToken.None);
        await bus.PublishAsync(envelope, CancellationToken.None);

        Assert.That(Inbox.Handled, Is.EqualTo(1));
        Assert.That(Inbox.Attempts, Is.EqualTo(1), "The duplicate is recognised before the handler runs.");
        Assert.That(await InboxCountAsync(), Is.EqualTo(1));
    }

    [Test]
    public async Task A_failed_handler_leaves_no_inbox_record_so_the_retry_runs_it_again()
    {
        var bus = CreateBus();
        var envelope = Envelope();
        Inbox.FailNext = true;

        Assert.ThrowsAsync<InvalidOperationException>(() => bus.PublishAsync(envelope, CancellationToken.None));
        Assert.That(await InboxCountAsync(), Is.Zero);

        await bus.PublishAsync(envelope, CancellationToken.None);

        Assert.That(Inbox.Attempts, Is.EqualTo(2));
        Assert.That(Inbox.Handled, Is.EqualTo(1));
        Assert.That(await InboxCountAsync(), Is.EqualTo(1));
    }
}

/// <summary>Captures the reason tag of every failed publication recorded by one <see cref="OutboxMetrics"/> instance.</summary>
public sealed class FailureReasons : IDisposable
{
    private readonly MeterListener _listener = new();

    public FailureReasons(OutboxMetrics metrics)
    {
        _listener.InstrumentPublished = (instrument, listener) =>
        {
            if (ReferenceEquals(instrument.Meter, metrics.Meter) && instrument.Name == "outbox.messages.failed")
            {
                listener.EnableMeasurementEvents(instrument);
            }
        };
        _listener.SetMeasurementEventCallback<long>((_, _, tags, _) =>
        {
            foreach (var tag in tags)
            {
                if (tag.Key == "reason")
                {
                    Reasons.Add((string)tag.Value!);
                }
            }
        });
        _listener.Start();
    }

    public List<string> Reasons { get; } = [];

    public void Dispose() => _listener.Dispose();
}

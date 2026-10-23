using BuildingBlocks.Infrastructure.Inbox;
using BuildingBlocks.Infrastructure.Messaging;
using BuildingBlocks.Infrastructure.Outbox;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace BuildingBlocks.Infrastructure.Tests;

public sealed class OutboxProcessorTests : InfrastructureTestBase
{
    private Task<OutboxRun> ProcessAsync() =>
        InScope(sp => sp.GetRequiredService<OutboxProcessor<TestDbContext>>().ProcessAsync(CancellationToken.None));

    private List<Guid> PublishedWidgetIds() =>
        [.. Bus.Published.Select(e => ((WidgetCreatedIntegrationEvent)Services.GetRequiredService<IntegrationEventTypes>().Deserialize(e)).WidgetId)];

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
        Bus.FailWhen = e => failing.Contains(((WidgetCreatedIntegrationEvent)Services.GetRequiredService<IntegrationEventTypes>().Deserialize(e)).WidgetId);

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
        Bus.FailWhen = e => ((WidgetCreatedIntegrationEvent)Services.GetRequiredService<IntegrationEventTypes>().Deserialize(e)).WidgetId == ids[0];
        await ProcessAsync();
        Bus.FailWhen = _ => false;

        Assert.That((await ProcessAsync()).Published, Is.Zero);

        Time.Advance(TimeSpan.FromSeconds(2));
        Assert.That((await ProcessAsync()).Published, Is.EqualTo(2));
        Assert.That(PublishedWidgetIds(), Is.EqualTo(ids));
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

using System.Text.Json;
using BuildingBlocks.Application;
using BuildingBlocks.Infrastructure.Outbox;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace BuildingBlocks.Infrastructure.Tests;

public sealed class SaveChangesTests : InfrastructureTestBase
{
    [Test]
    public async Task Domain_events_are_handled_before_the_change_is_committed()
    {
        bool? committedWhileHandling = null;
        Spy.Behaviour = async _ => committedWhileHandling = await InScope(sp => sp.GetRequiredService<TestDbContext>().Widgets.AnyAsync());

        var widget = await SaveNewWidgetAsync();

        Assert.That(Spy.Handled.Single(), Is.EqualTo(new WidgetCreated(widget.Id, Start)));
        Assert.That(committedWhileHandling, Is.False, "Handlers run inside the unit of work, before the commit.");
        Assert.That(widget.DomainEvents, Is.Empty, "Dispatched events are cleared.");
        Assert.That(await InScope(sp => sp.GetRequiredService<TestDbContext>().Widgets.AnyAsync()), Is.True);
    }

    [Test]
    public async Task Events_raised_by_a_handler_are_dispatched_in_the_same_save()
    {
        var widget = Widget.Create("widget", Start);
        Spy.Behaviour = e =>
        {
            if (e is WidgetCreated)
            {
                widget.Ping(Start);
            }

            return Task.CompletedTask;
        };

        await InScope(async sp =>
        {
            var db = sp.GetRequiredService<TestDbContext>();
            db.Widgets.Add(widget);
            await db.SaveChangesAsync();
        });

        Assert.That(Spy.Handled.Select(e => e.GetType()), Is.EqualTo(new[] { typeof(WidgetCreated), typeof(WidgetPinged) }));
    }

    [Test]
    public async Task Handlers_that_keep_raising_events_are_stopped_and_nothing_is_saved()
    {
        var widget = Widget.Create("widget", Start);
        Spy.Behaviour = _ =>
        {
            widget.Ping(Start);
            return Task.CompletedTask;
        };

        var exception = Assert.ThrowsAsync<InvalidOperationException>(() => InScope(async sp =>
        {
            var db = sp.GetRequiredService<TestDbContext>();
            db.Widgets.Add(widget);
            await db.SaveChangesAsync();
        }));

        Assert.That(exception!.Message, Does.Contain("5 rounds"));
        Assert.That(await InScope(sp => sp.GetRequiredService<TestDbContext>().Widgets.AnyAsync()), Is.False);
    }

    [Test]
    public async Task Integration_events_put_in_the_outbox_are_stored_with_the_change()
    {
        Spy.Behaviour = _ => Task.CompletedTask;
        Guid widgetId = default;

        await InScope(async sp =>
        {
            var outbox = sp.GetRequiredService<IOutbox>();
            Spy.Behaviour = e =>
            {
                outbox.Enqueue(new WidgetCreatedIntegrationEvent(((WidgetCreated)e).WidgetId, e.OccurredAt), "widget-group");
                return Task.CompletedTask;
            };

            var widget = Widget.Create("widget", Start);
            widgetId = widget.Id;
            var db = sp.GetRequiredService<TestDbContext>();
            db.Widgets.Add(widget);
            await db.SaveChangesAsync();
        });

        var message = (await OutboxMessagesAsync()).Single();
        Assert.Multiple(() =>
        {
            Assert.That(message.Type, Is.EqualTo("test.widget-created.v1"));
            Assert.That(message.MessageGroup, Is.EqualTo("widget-group"));
            Assert.That(message.OccurredAt, Is.EqualTo(Start));
            Assert.That(message.PublishedAt, Is.Null);
            Assert.That(JsonDocument.Parse(message.Payload).RootElement.GetProperty("widgetId").GetGuid(), Is.EqualTo(widgetId));
        });
    }

    [Test]
    public async Task Changing_only_something_inside_the_aggregate_increments_its_version()
    {
        var widget = await SaveNewWidgetAsync("wheel");

        await InScope(async sp =>
        {
            var db = sp.GetRequiredService<TestDbContext>();
            var loaded = await db.Widgets.SingleAsync(w => w.Id == widget.Id);
            loaded.RenamePart(0, "big wheel");
            await db.SaveChangesAsync();
        });
        await InScope(async sp =>
        {
            var db = sp.GetRequiredService<TestDbContext>();
            var loaded = await db.Widgets.SingleAsync(w => w.Id == widget.Id);
            loaded.AddPart("bell");
            await db.SaveChangesAsync();
        });
        await InScope(async sp =>
        {
            var db = sp.GetRequiredService<TestDbContext>();
            await db.Widgets.SingleAsync(w => w.Id == widget.Id);
            await db.SaveChangesAsync();
        });

        var stored = await InScope(sp => sp.GetRequiredService<TestDbContext>().Widgets.AsNoTracking().SingleAsync(w => w.Id == widget.Id));
        Assert.That(stored.Version, Is.EqualTo(2), "One increment per changing save; a save without changes leaves it alone.");
        Assert.That(stored.Parts.Select(p => p.Name), Is.EquivalentTo(new[] { "big wheel", "bell" }));
    }

    [Test]
    public async Task A_concurrent_change_is_refused_and_its_outbox_messages_are_not_stored()
    {
        var widget = await SaveNewWidgetAsync("wheel");
        await using var first = Services.CreateAsyncScope();
        await using var second = Services.CreateAsyncScope();
        var firstDb = first.ServiceProvider.GetRequiredService<TestDbContext>();
        var secondDb = second.ServiceProvider.GetRequiredService<TestDbContext>();
        var firstCopy = await firstDb.Widgets.SingleAsync(w => w.Id == widget.Id);
        var secondCopy = await secondDb.Widgets.SingleAsync(w => w.Id == widget.Id);

        firstCopy.RenamePart(0, "first");
        await firstDb.SaveChangesAsync();

        var secondOutbox = second.ServiceProvider.GetRequiredService<IOutbox>();
        Spy.Behaviour = e =>
        {
            secondOutbox.Enqueue(new WidgetCreatedIntegrationEvent(widget.Id, e.OccurredAt));
            return Task.CompletedTask;
        };
        secondCopy.RenamePart(0, "second");
        secondCopy.Ping(Start);

        Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => secondDb.SaveChangesAsync());
        Assert.That(await OutboxMessagesAsync(), Is.Empty, "The outbox message rolls back with the change that caused it.");
    }
}

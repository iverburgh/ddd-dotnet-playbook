using BuildingBlocks.Application;
using BuildingBlocks.Contracts;
using BuildingBlocks.Domain;
using BuildingBlocks.Infrastructure.DomainEvents;
using BuildingBlocks.Infrastructure.Messaging;
using BuildingBlocks.Infrastructure.Outbox;
using BuildingBlocks.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace BuildingBlocks.Infrastructure.Tests;

// A tiny aggregate with an owned child collection, just enough to exercise the plumbing.

public sealed record WidgetCreated(Guid WidgetId, DateTimeOffset OccurredAt) : IDomainEvent;

public sealed record WidgetPinged(Guid WidgetId, DateTimeOffset OccurredAt) : IDomainEvent;

[IntegrationEventName("test.widget-created.v1")]
public sealed record WidgetCreatedIntegrationEvent(Guid WidgetId, DateTimeOffset OccurredAt) : IIntegrationEvent;

public sealed class Part
{
    public Part(string name) => Name = name;

    public string Name { get; set; }
}

public sealed class Widget : AggregateRoot<Guid>
{
    private readonly List<Part> _parts = [];

    private Widget(Guid id, string name)
        : base(id) => Name = name;

    private Widget() => Name = string.Empty;

    public string Name { get; private set; }

    public IReadOnlyList<Part> Parts => _parts;

    public static Widget Create(string name, DateTimeOffset now)
    {
        var widget = new Widget(Guid.NewGuid(), name);
        widget.Raise(new WidgetCreated(widget.Id, now));
        return widget;
    }

    public void Rename(string name) => Name = name;

    public void AddPart(string name) => _parts.Add(new Part(name));

    public void RenamePart(int index, string name) => _parts[index].Name = name;

    public void Ping(DateTimeOffset now) => Raise(new WidgetPinged(Id, now));
}

public sealed class TestDbContext(
    DbContextOptions<TestDbContext> options,
    IDomainEventDispatcher dispatcher,
    OutboxCollector outbox,
    OutboxSignal signal) : DbContextBase(options, dispatcher, outbox, signal)
{
    public DbSet<Widget> Widgets => Set<Widget>();

    protected override string TablePrefix => "Test";

    protected override void ConfigureModel(ModelBuilder modelBuilder) =>
        modelBuilder.Entity<Widget>(widget =>
        {
            widget.HasKey(w => w.Id);
            widget.OwnsMany(w => w.Parts, part =>
            {
                part.WithOwner().HasForeignKey("WidgetId");
                part.Property<int>("Id");
                part.HasKey("Id");
            });
            widget.Navigation(w => w.Parts).HasField("_parts").UsePropertyAccessMode(PropertyAccessMode.Field);
        });
}

/// <summary>Lets each test decide what the domain event handlers do, and records what they saw.</summary>
public sealed class HandlerSpy
{
    public List<IDomainEvent> Handled { get; } = [];

    public Func<IDomainEvent, Task> Behaviour { get; set; } = _ => Task.CompletedTask;
}

public sealed class SpyHandler(HandlerSpy spy) : IDomainEventHandler<WidgetCreated>, IDomainEventHandler<WidgetPinged>
{
    public Task Handle(WidgetCreated domainEvent, CancellationToken cancellationToken) => Record(domainEvent);

    public Task Handle(WidgetPinged domainEvent, CancellationToken cancellationToken) => Record(domainEvent);

    private Task Record(IDomainEvent domainEvent)
    {
        spy.Handled.Add(domainEvent);
        return spy.Behaviour(domainEvent);
    }
}

/// <summary>A broker that records what it was given and can be told to fail for chosen messages.</summary>
public sealed class FakeMessageBus : IMessageBus
{
    public List<MessageEnvelope> Published { get; } = [];

    public Func<MessageEnvelope, bool> FailWhen { get; set; } = _ => false;

    public Task PublishAsync(MessageEnvelope envelope, CancellationToken cancellationToken)
    {
        if (FailWhen(envelope))
        {
            throw new InvalidOperationException("Broker unavailable");
        }

        Published.Add(envelope);
        return Task.CompletedTask;
    }
}

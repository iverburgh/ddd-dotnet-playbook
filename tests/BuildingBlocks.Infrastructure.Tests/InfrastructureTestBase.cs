using BuildingBlocks.Application;
using BuildingBlocks.Infrastructure.Outbox;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

namespace BuildingBlocks.Infrastructure.Tests;

/// <summary>
/// Real EF Core on in-memory SQLite, a fake clock and a fake broker. One open connection per test
/// keeps the in-memory database alive and lets several scopes (and DbContexts) see the same data.
/// </summary>
public abstract class InfrastructureTestBase
{
    protected static readonly DateTimeOffset Start = new(2025, 1, 1, 12, 0, 0, TimeSpan.Zero);

    private SqliteConnection _connection = null!;

    protected FakeTimeProvider Time { get; private set; } = null!;

    protected HandlerSpy Spy { get; private set; } = null!;

    protected InboxSpy Inbox { get; private set; } = null!;

    protected FakeMessageBus Bus { get; private set; } = null!;

    protected ServiceProvider Services { get; private set; } = null!;

    [SetUp]
    public async Task SetUpInfrastructure()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        await _connection.OpenAsync();

        Time = new FakeTimeProvider(Start);
        Spy = new HandlerSpy();
        Inbox = new InboxSpy();
        Bus = new FakeMessageBus();

        var services = new ServiceCollection();
        services.AddSingleton<TimeProvider>(Time);
        services.AddSingleton<Messaging.IMessageBus>(Bus);
        services.AddSingleton(Spy);
        services.AddSingleton(Inbox);
        services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));
        services.AddBuildingBlocks();
        services.AddDbContext<TestDbContext>(options => options.UseSqlite(_connection));
        services.AddDomainEventHandlers(typeof(SpyHandler).Assembly);
        services.AddIntegrationEvents(typeof(WidgetCreatedIntegrationEvent).Assembly);
        services.AddIntegrationEventHandler<WidgetCreatedIntegrationEvent, CountingIntegrationHandler, TestDbContext>();
        services.AddOutboxPublisher<TestDbContext>();
        services.Configure<OutboxOptions>(options =>
        {
            options.RetryBaseDelay = TimeSpan.FromSeconds(2);
            options.RetryMaxDelay = TimeSpan.FromSeconds(30);
        });

        Services = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });

        await InScope(sp => sp.GetRequiredService<TestDbContext>().Database.EnsureCreatedAsync());
    }

    [TearDown]
    public async Task TearDownInfrastructure()
    {
        await Services.DisposeAsync();
        await _connection.DisposeAsync();
    }

    protected async Task InScope(Func<IServiceProvider, Task> action)
    {
        await using var scope = Services.CreateAsyncScope();
        await action(scope.ServiceProvider);
    }

    protected async Task<T> InScope<T>(Func<IServiceProvider, Task<T>> action)
    {
        await using var scope = Services.CreateAsyncScope();
        return await action(scope.ServiceProvider);
    }

    protected Task<Widget> SaveNewWidgetAsync(params string[] parts) => InScope(async sp =>
    {
        var widget = Widget.Create("widget", Time.GetUtcNow());
        foreach (var part in parts)
        {
            widget.AddPart(part);
        }

        var db = sp.GetRequiredService<TestDbContext>();
        db.Widgets.Add(widget);
        await db.SaveChangesAsync();
        return widget;
    });

    /// <summary>Stores one outbox message per group entry, in that order, and returns their widget ids.</summary>
    protected Task<List<Guid>> EnqueueAsync(params string?[] groups) => InScope(async sp =>
    {
        var outbox = sp.GetRequiredService<IOutbox>();
        var ids = new List<Guid>();
        foreach (var group in groups)
        {
            var id = Guid.NewGuid();
            ids.Add(id);
            outbox.Enqueue(new WidgetCreatedIntegrationEvent(id, Time.GetUtcNow()), group);
        }

        await sp.GetRequiredService<TestDbContext>().SaveChangesAsync();
        return ids;
    });

    protected Task<List<OutboxMessage>> OutboxMessagesAsync() =>
        InScope(sp => sp.GetRequiredService<TestDbContext>().Set<OutboxMessage>().AsNoTracking().OrderBy(m => m.Id).ToListAsync());
}

public sealed class InboxSpy
{
    public int Attempts { get; set; }

    public int Handled { get; set; }

    public bool FailNext { get; set; }
}

public sealed class CountingIntegrationHandler(InboxSpy spy) : IIntegrationEventHandler<WidgetCreatedIntegrationEvent>
{
    public Task Handle(WidgetCreatedIntegrationEvent integrationEvent, CancellationToken cancellationToken)
    {
        spy.Attempts++;
        if (spy.FailNext)
        {
            spy.FailNext = false;
            throw new InvalidOperationException("Handler failed");
        }

        spy.Handled++;
        return Task.CompletedTask;
    }
}

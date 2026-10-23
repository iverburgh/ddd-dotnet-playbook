using BuildingBlocks.Domain;
using BuildingBlocks.Infrastructure.DomainEvents;
using BuildingBlocks.Infrastructure.Inbox;
using BuildingBlocks.Infrastructure.Outbox;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace BuildingBlocks.Infrastructure.Persistence;

/// <summary>
/// Base for the DbContext of every bounded context. Saving does, in one transaction:
/// <list type="number">
/// <item>dispatch the domain events of every tracked aggregate, repeating while handlers raise new ones;</item>
/// <item>store the integration events that handlers put in the outbox;</item>
/// <item>increment the version of every aggregate that changed, including changes inside it only;</item>
/// <item>commit, and wake the outbox publisher.</item>
/// </list>
/// </summary>
public abstract class DbContextBase : DbContext
{
    private const int MaxDispatchRounds = 5;

    private readonly IDomainEventDispatcher _dispatcher;
    private readonly OutboxCollector _outbox;
    private readonly OutboxSignal _signal;

    protected DbContextBase(DbContextOptions options, IDomainEventDispatcher dispatcher, OutboxCollector outbox, OutboxSignal signal)
        : base(options)
    {
        _dispatcher = dispatcher;
        _outbox = outbox;
        _signal = signal;
    }

    /// <summary>Prefix for the outbox and inbox tables, so several contexts can share one database.</summary>
    protected abstract string TablePrefix { get; }

    /// <summary>Map the aggregates of this bounded context. Map everything inside an aggregate as owned types, so changes inside it count as changes to the aggregate.</summary>
    protected abstract void ConfigureModel(ModelBuilder modelBuilder);

    public override int SaveChanges(bool acceptAllChangesOnSuccess) =>
        throw new NotSupportedException("Use SaveChangesAsync: domain event handlers are asynchronous.");

    public override async Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
    {
        await DispatchDomainEventsAsync(cancellationToken);

        var messages = _outbox.Drain();
        Set<OutboxMessage>().AddRange(messages);
        IncrementAggregateVersions();

        var result = await base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
        if (messages.Count > 0)
        {
            _signal.Notify(GetType());
        }

        return result;
    }

    protected sealed override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ConfigureModel(modelBuilder);

        modelBuilder.Entity<OutboxMessage>(outbox =>
        {
            outbox.ToTable($"{TablePrefix}OutboxMessages");
            outbox.HasKey(m => m.Id);
            outbox.Property(m => m.Id).ValueGeneratedOnAdd();
            outbox.HasIndex(m => m.MessageId).IsUnique();
            outbox.HasIndex(m => m.PublishedAt);
            outbox.Property(m => m.Type).HasMaxLength(200);
            outbox.Property(m => m.MessageGroup).HasMaxLength(200);
        });

        modelBuilder.Entity<InboxMessage>(inbox =>
        {
            inbox.ToTable($"{TablePrefix}InboxMessages");
            inbox.HasKey(m => new { m.MessageId, m.Handler });
            inbox.Property(m => m.Handler).HasMaxLength(400);
        });

        foreach (var aggregate in modelBuilder.Model.GetEntityTypes().Where(t => typeof(IAggregateRoot).IsAssignableFrom(t.ClrType)).ToList())
        {
            var builder = modelBuilder.Entity(aggregate.ClrType);
            builder.Ignore(nameof(IAggregateRoot.DomainEvents));
            builder.Property(nameof(IAggregateRoot.Version)).IsConcurrencyToken();
        }
    }

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        configurationBuilder.IgnoreAny<IDomainEvent>();

        // SQLite cannot order or compare DateTimeOffset; ISO 8601 strings in UTC sort chronologically.
        if (Database.ProviderName?.Contains("Sqlite", StringComparison.OrdinalIgnoreCase) == true)
        {
            configurationBuilder.Properties<DateTimeOffset>().HaveConversion<DateTimeOffsetToStringConverter>();
        }
    }

    private async Task DispatchDomainEventsAsync(CancellationToken cancellationToken)
    {
        for (var round = 1; ; round++)
        {
            var aggregates = ChangeTracker.Entries<IAggregateRoot>()
                .Select(entry => entry.Entity)
                .Where(aggregate => aggregate.DomainEvents.Count > 0)
                .ToList();
            if (aggregates.Count == 0)
            {
                return;
            }

            if (round > MaxDispatchRounds)
            {
                throw new InvalidOperationException(
                    $"Domain event handlers were still raising new events after {MaxDispatchRounds} rounds; look for handlers that trigger each other.");
            }

            var events = aggregates.SelectMany(aggregate => aggregate.DomainEvents).ToList();
            aggregates.ForEach(aggregate => aggregate.ClearDomainEvents());

            foreach (var domainEvent in events)
            {
                await _dispatcher.DispatchAsync(domainEvent, cancellationToken);
            }
        }
    }

    private void IncrementAggregateVersions()
    {
        var incremented = new HashSet<object>(ReferenceEqualityComparer.Instance);
        var changed = ChangeTracker.Entries()
            .Where(e => e.State is EntityState.Added or EntityState.Modified or EntityState.Deleted)
            .ToList();

        foreach (var entry in changed)
        {
            var root = FindAggregateRoot(entry);
            if (root is { State: EntityState.Modified or EntityState.Unchanged } && incremented.Add(root.Entity))
            {
                var version = root.Property(nameof(IAggregateRoot.Version));
                version.CurrentValue = (int)version.CurrentValue! + 1;
            }
        }
    }

    private EntityEntry? FindAggregateRoot(EntityEntry entry)
    {
        var current = entry;
        while (current is not null && current.Entity is not IAggregateRoot)
        {
            var ownership = current.Metadata.FindOwnership();
            current = ownership is null ? null : FindOwner(current, ownership);
        }

        return current;
    }

    private EntityEntry? FindOwner(EntityEntry owned, IForeignKey ownership)
    {
        var keyValues = ownership.Properties.Select(p => owned.Property(p.Name).CurrentValue).ToArray();
        return ChangeTracker.Entries().FirstOrDefault(candidate =>
            candidate.Metadata.Name == ownership.PrincipalEntityType.Name &&
            ownership.PrincipalKey.Properties.Select(p => candidate.Property(p.Name).CurrentValue).SequenceEqual(keyValues));
    }
}

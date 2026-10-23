using System.Collections.Concurrent;
using System.Threading.Channels;

namespace BuildingBlocks.Infrastructure.Outbox;

/// <summary>
/// Wakes the publisher of a context as soon as a transaction commits outbox messages, instead of
/// waiting for the next poll. Each context has its own channel, so a commit in one context never
/// consumes the wake-up meant for another. A burst of commits collapses into one pending signal.
/// </summary>
public sealed class OutboxSignal
{
    private readonly ConcurrentDictionary<Type, Channel<bool>> _channels = new();

    public void Notify(Type context) => ChannelFor(context).Writer.TryWrite(true);

    /// <summary>Returns when <paramref name="context"/> is signalled or when <paramref name="timeout"/> has passed, whichever comes first.</summary>
    public async Task WaitAsync(Type context, TimeSpan timeout, CancellationToken cancellationToken)
    {
        using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutSource.CancelAfter(timeout);
        try
        {
            await ChannelFor(context).Reader.ReadAsync(timeoutSource.Token);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            // The timeout passed without a signal: poll anyway.
        }
    }

    private Channel<bool> ChannelFor(Type context) => _channels.GetOrAdd(
        context,
        _ => Channel.CreateBounded<bool>(new BoundedChannelOptions(1) { FullMode = BoundedChannelFullMode.DropWrite }));
}

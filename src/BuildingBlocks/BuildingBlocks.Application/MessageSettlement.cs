using BuildingBlocks.Domain;

namespace BuildingBlocks.Application;

/// <summary>What a message consumer should do with a message after its handler returned a result.</summary>
public enum SettlementAction
{
    /// <summary>Done: remove the message.</summary>
    Complete,

    /// <summary>Try again later, unchanged: abandon the message (or schedule it) so the broker redelivers it.</summary>
    Retry,

    /// <summary>Will fail on every delivery: move it to the dead-letter queue with the error's code as reason.</summary>
    DeadLetter,
}

/// <summary>
/// Translates a handler's <see cref="Result"/> into a settlement decision, by error kind. This is the messaging
/// counterpart of the API's HTTP mapping: the same typed error that becomes a <c>503</c> over HTTP becomes a retry
/// on a queue, and the same business-rule error that becomes a <c>422</c> becomes a dead-letter.
/// </summary>
/// <remarks>
/// Only <em>returned</em> failures are classified here. Thrown exceptions, delivery counts, lock loss and shutdown
/// belong to the trigger's settlement policy; see the Service Bus settlement playbook linked from
/// <c>docs/error-handling.md</c>. A retryable error still counts as a delivery, so the broker's
/// <c>maxDeliveryCount</c> eventually dead-letters a dependency that never comes back.
/// </remarks>
/// <param name="Action">What to do.</param>
/// <param name="ReasonCode">The error code, used as dead-letter reason or log property; null on success.</param>
/// <param name="Description">The error message, used as dead-letter description; null on success.</param>
/// <param name="RetryAfter">The error's hint for when to try again, if it has one.</param>
public sealed record SettlementDecision(SettlementAction Action, string? ReasonCode, string? Description, TimeSpan? RetryAfter)
{
    public static SettlementDecision Complete { get; } = new(SettlementAction.Complete, null, null, null);

    public static SettlementDecision For(Result result)
    {
        Guard.NotNull(result);

        if (result.IsSuccess)
        {
            return Complete;
        }

        var error = result.Error;
        return error is IRetryableError retryable
            ? new SettlementDecision(SettlementAction.Retry, error.Code, error.Message, retryable.RetryAfter)
            : new SettlementDecision(SettlementAction.DeadLetter, error.Code, error.Message, null);
    }
}

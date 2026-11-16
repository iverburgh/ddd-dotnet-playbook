using BuildingBlocks.Domain;

namespace BuildingBlocks.Application.Tests;

/// <summary>
/// The messaging side of the taxonomy: what a consumer does with a returned failure. Exceptions and delivery counts
/// are the trigger's concern (see the settlement playbook); these tests cover only the classification by kind.
/// </summary>
public sealed class SettlementDecisionTests
{
    [Test]
    public void A_success_completes_the_message()
    {
        var decision = SettlementDecision.For(Result.Success());

        Assert.That(decision, Is.EqualTo(new SettlementDecision(SettlementAction.Complete, null, null, null)));
    }

    [Test]
    public void A_retryable_failure_is_retried_with_its_hint()
    {
        var error = new TransientError("payment.provider-unavailable", "The payment provider timed out.", TimeSpan.FromSeconds(30));

        var decision = SettlementDecision.For(Result.Failure(error));

        Assert.That(decision, Is.EqualTo(new SettlementDecision(SettlementAction.Retry, error.Code, error.Message, TimeSpan.FromSeconds(30))));
    }

    [Test]
    public void A_rate_limit_is_retried_although_it_is_a_client_error()
    {
        var decision = SettlementDecision.For(Result.Failure(new RateLimitedError("carrier.rate-limited", "Slow down.")));

        Assert.That(decision.Action, Is.EqualTo(SettlementAction.Retry));
    }

    [TestCaseSource(nameof(NonRetryable))]
    public void A_non_retryable_failure_is_dead_lettered_with_its_code_as_reason(Error error)
    {
        var decision = SettlementDecision.For(Result.Failure<Guid>(error));

        Assert.That(decision, Is.EqualTo(new SettlementDecision(SettlementAction.DeadLetter, error.Code, error.Message, null)));
    }

    private static IEnumerable<Error> NonRetryable() =>
    [
        new ValidationError("order.invalid", "Invalid."),
        new ForbiddenError("order.forbidden", "Not yours."),
        new NotFoundError("order.not-found", "Gone."),
        new ConflictError("order.conflict", "Changed."),
        new BusinessRuleError("order.already-shipped", "Too late."),
        new UnexpectedError("unexpected-error", "Bug."),
    ];
}

using BuildingBlocks.Domain;

namespace BuildingBlocks.Application.Tests;

public sealed class ErrorTests
{
    [TestCase("order.not-draft")]
    [TestCase("invalid-input")]
    [TestCase("shipment.v2.already-dispatched")]
    public void A_lower_case_dotted_kebab_case_code_is_accepted(string code)
    {
        Assert.That(new BusinessRuleError(code, "No.").Code, Is.EqualTo(code));
    }

    [TestCase("")]
    [TestCase("Order.NotDraft")]
    [TestCase("order not draft")]
    [TestCase("order..not-draft")]
    [TestCase("order.")]
    [TestCase("1order")]
    public void A_code_that_is_not_lower_case_dotted_kebab_case_is_a_bug(string code)
    {
        Assert.That(() => new BusinessRuleError(code, "No."), Throws.ArgumentException);
    }

    [TestCase("")]
    [TestCase("  ")]
    public void An_error_needs_a_message(string message)
    {
        Assert.That(() => new NotFoundError("order.not-found", message), Throws.ArgumentException);
    }

    [Test]
    public void Every_kind_is_either_a_client_or_a_server_error_and_only_some_are_retryable()
    {
        Error[] clientErrors =
        [
            new ValidationError("a", "m"), new ForbiddenError("a", "m"), new NotFoundError("a", "m"),
            new ConflictError("a", "m"), new BusinessRuleError("a", "m"), new RateLimitedError("a", "m"),
        ];
        Error[] serverErrors = [new TransientError("a", "m"), new UnexpectedError("a", "m")];

        Assert.Multiple(() =>
        {
            Assert.That(clientErrors, Is.All.InstanceOf<ClientError>());
            Assert.That(serverErrors, Is.All.InstanceOf<ServerError>());
            Assert.That(
                clientErrors.Concat(serverErrors).Where(e => e is IRetryableError).Select(e => e.GetType()),
                Is.EquivalentTo(new[] { typeof(RateLimitedError), typeof(TransientError) }));
        });
    }

    [Test]
    public void Errors_with_the_same_kind_code_and_message_are_equal()
    {
        Assert.Multiple(() =>
        {
            Assert.That(new NotFoundError("order.not-found", "Gone."), Is.EqualTo(new NotFoundError("order.not-found", "Gone.")));
            Assert.That(new NotFoundError("order.not-found", "Gone."), Is.Not.EqualTo(new ConflictError("order.not-found", "Gone.")));
        });
    }

    [Test]
    public void A_failure_needs_an_error_and_a_failed_result_has_no_value()
    {
        var failure = Result.Failure<int>(new NotFoundError("order.not-found", "Gone."));

        Assert.Multiple(() =>
        {
            Assert.That(() => Result.Failure(null!), Throws.ArgumentNullException);
            Assert.That(() => failure.Value, Throws.InvalidOperationException.With.Message.Contains("order.not-found"));
        });
    }

    [Test]
    public void A_guard_names_the_argument_it_rejected()
    {
        string? customerName = null;

        Assert.That(() => Guard.NotNull(customerName), Throws.ArgumentNullException.With.Property("ParamName").EqualTo("customerName"));
    }
}

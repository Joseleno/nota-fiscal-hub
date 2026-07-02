using FluentValidation;
using Mediator;
using NSubstitute;
using Shouldly;
using VisuFiscalHub.Application.Common.Behaviors;
using VisuFiscalHub.Domain.Common;

namespace VisuFiscalHub.Tests.Application;

public class ValidationBehaviorTests
{
    private sealed record TestCommand : ICommand<Result<string>>;

    private sealed class AlwaysValidValidator : AbstractValidator<TestCommand>
    {
        public AlwaysValidValidator() { }
    }

    private sealed class AlwaysFailValidator : AbstractValidator<TestCommand>
    {
        public AlwaysFailValidator()
        {
            RuleFor(x => x).Must(_ => false).WithMessage("Validação falhou propositalmente.");
        }
    }

    [Fact]
    public async Task Handle_SemValidators_DevePassarParaHandler()
    {
        var behavior = new ValidationBehavior<TestCommand, Result<string>>([]);
        var next = Substitute.For<MessageHandlerDelegate<TestCommand, Result<string>>>();
        next(Arg.Any<TestCommand>(), Arg.Any<CancellationToken>())
            .Returns(Result.Success("ok"));

        var result = await behavior.Handle(new TestCommand(), next, CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        result.Value.ShouldBe("ok");
        await next.Received(1)(Arg.Any<TestCommand>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_ValidatorValido_DevePassarParaHandler()
    {
        var behavior = new ValidationBehavior<TestCommand, Result<string>>([new AlwaysValidValidator()]);
        var next = Substitute.For<MessageHandlerDelegate<TestCommand, Result<string>>>();
        next(Arg.Any<TestCommand>(), Arg.Any<CancellationToken>())
            .Returns(Result.Success("ok"));

        var result = await behavior.Handle(new TestCommand(), next, CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        await next.Received(1)(Arg.Any<TestCommand>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_ValidatorFalha_DeveRetornarFailureSemChamarHandler()
    {
        var behavior = new ValidationBehavior<TestCommand, Result<string>>([new AlwaysFailValidator()]);
        var next = Substitute.For<MessageHandlerDelegate<TestCommand, Result<string>>>();

        var result = await behavior.Handle(new TestCommand(), next, CancellationToken.None);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("Validation.Failed");
        result.Error.Message.ShouldContain("Validação falhou propositalmente.");
        await next.DidNotReceiveWithAnyArgs()(default!, default);
    }

    [Fact]
    public async Task Handle_MultiploValidatoresFalham_DeveConcatenarMensagens()
    {
        var behavior = new ValidationBehavior<TestCommand, Result<string>>([new AlwaysFailValidator(), new AlwaysFailValidator()]);
        var next = Substitute.For<MessageHandlerDelegate<TestCommand, Result<string>>>();

        var result = await behavior.Handle(new TestCommand(), next, CancellationToken.None);

        result.IsFailure.ShouldBeTrue();
        result.Error.Message.ShouldContain("; ");
    }
}

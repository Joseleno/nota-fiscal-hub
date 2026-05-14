using System.Collections.Concurrent;
using System.Reflection;
using FluentValidation;
using Mediator;
using VisuFiscalHub.Domain.Common;

namespace VisuFiscalHub.Application.Common.Behaviors;

public sealed class ValidationBehavior<TMessage, TResponse>
    : IPipelineBehavior<TMessage, TResponse>
    where TMessage : IMessage
    where TResponse : Result
{
    // Cache de delegates para evitar reflection em cada chamada.
    // Chave: tipo concreto de TResponse; valor: factory que cria Result.Failure para aquele tipo.
    private static readonly ConcurrentDictionary<Type, Func<Error, Result>> FailureFactoryCache = new();

    private readonly IEnumerable<IValidator<TMessage>> _validators;

    public ValidationBehavior(IEnumerable<IValidator<TMessage>> validators)
    {
        _validators = validators;
    }

    public async ValueTask<TResponse> Handle(
        TMessage message,
        MessageHandlerDelegate<TMessage, TResponse> next,
        CancellationToken cancellationToken)
    {
        if (!_validators.Any())
            return await next(message, cancellationToken);

        var errors = new List<string>();
        foreach (var validator in _validators)
        {
            var validationResult = await validator.ValidateAsync(message, cancellationToken);
            if (!validationResult.IsValid)
                errors.AddRange(validationResult.Errors.Select(e => e.ErrorMessage));
        }

        if (errors.Count == 0)
            return await next(message, cancellationToken);

        var error = new Error("Validation.Failed", string.Join("; ", errors));
        return (TResponse)CreateFailureResult(typeof(TResponse), error);
    }

    private static Result CreateFailureResult(Type responseType, Error error)
    {
        // Non-generic Result — handled directly without reflection.
        if (responseType == typeof(Result))
            return Result.Failure(error);

        var factory = FailureFactoryCache.GetOrAdd(responseType, BuildFactory);
        return factory(error);
    }

    private static Func<Error, Result> BuildFactory(Type responseType)
    {
        var genericArgs = responseType.GenericTypeArguments;
        if (genericArgs.Length == 0)
            // Fallback: treat as non-generic Result — factory can only be invoked when TResponse : Result.
            return e => Result.Failure(e);

        var failureMethod = typeof(Result)
            .GetMethods(BindingFlags.Public | BindingFlags.Static)
            .FirstOrDefault(m => m.Name == "Failure" && m.IsGenericMethod && m.GetParameters().Length == 1);

        if (failureMethod is null)
            return e => Result.Failure(e);

        var closedMethod = failureMethod.MakeGenericMethod(genericArgs[0]);
        return e => (Result)closedMethod.Invoke(null, [e])!;
    }
}

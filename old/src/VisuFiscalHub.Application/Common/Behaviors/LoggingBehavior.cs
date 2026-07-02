using System.Diagnostics;
using Mediator;
using Microsoft.Extensions.Logging;
using VisuFiscalHub.Domain.Common;

namespace VisuFiscalHub.Application.Common.Behaviors;

public sealed class LoggingBehavior<TMessage, TResponse>
    : IPipelineBehavior<TMessage, TResponse>
    where TMessage : IMessage
    where TResponse : Result
{
    private readonly ILogger<LoggingBehavior<TMessage, TResponse>> _logger;

    public LoggingBehavior(ILogger<LoggingBehavior<TMessage, TResponse>> logger)
    {
        _logger = logger;
    }

    public async ValueTask<TResponse> Handle(
        TMessage message,
        MessageHandlerDelegate<TMessage, TResponse> next,
        CancellationToken cancellationToken)
    {
        var requestName = typeof(TMessage).Name;
        _logger.LogInformation("Handling {RequestType}", requestName);

        var sw = Stopwatch.StartNew();
        var response = await next(message, cancellationToken);
        sw.Stop();

        if (response.IsFailure)
        {
            _logger.LogWarning(
                "Request {RequestType} falhou em {ElapsedMs}ms — {ErrorCode}: {ErrorMessage}",
                requestName, sw.ElapsedMilliseconds, response.Error.Code, response.Error.Message);
        }
        else
        {
            _logger.LogInformation(
                "Request {RequestType} concluído em {ElapsedMs}ms",
                requestName, sw.ElapsedMilliseconds);
        }

        return response;
    }
}

using FluentValidation;
using Mediator;
using Microsoft.Extensions.DependencyInjection;
using VisuFiscalHub.Application.Common.Behaviors;

namespace VisuFiscalHub.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        // PipelineBehaviors array = ordered outermost→innermost.
        // LoggingBehavior outermost: captures all failures including validation failures.
        // ValidationBehavior innermost: short-circuits before handler on invalid input.
        services.AddMediator(options =>
        {
            options.ServiceLifetime = ServiceLifetime.Scoped;
            options.PipelineBehaviors =
            [
                typeof(LoggingBehavior<,>),
                typeof(ValidationBehavior<,>)
            ];
        });

        services.AddValidatorsFromAssembly(
            typeof(DependencyInjection).Assembly,
            includeInternalTypes: true);

        return services;
    }
}

namespace VisuFiscalHub.Application.Common.Interfaces;

public interface ICorrelationContext
{
    string? CorrelationId { get; }
}

using Mediator;
using Microsoft.Extensions.Logging;
using VisuFiscalHub.Domain.Events;

namespace VisuFiscalHub.Application.Documents.EventHandlers;

internal sealed class DocumentoFiscalDenegadoEventHandler
    : INotificationHandler<DocumentoFiscalDenegadoEvent>
{
    private readonly ILogger<DocumentoFiscalDenegadoEventHandler> _logger;

    public DocumentoFiscalDenegadoEventHandler(
        ILogger<DocumentoFiscalDenegadoEventHandler> logger)
    {
        _logger = logger;
    }

    public ValueTask Handle(
        DocumentoFiscalDenegadoEvent notification,
        CancellationToken cancellationToken)
    {
        // Denegação é irreversível — nota não pode ser reemitida com o mesmo CNPJ.
        // Nível Critical para alertar equipe operacional imediatamente.
        _logger.LogCritical(
            "Documento fiscal DENEGADO. DocumentoId={DocumentoId} TenantId={TenantId} Cnpj={Cnpj} Motivo={XMotivo}",
            notification.DocumentoFiscalId,
            notification.TenantId,
            notification.Cnpj,
            notification.XMotivo);

        return ValueTask.CompletedTask;
    }
}

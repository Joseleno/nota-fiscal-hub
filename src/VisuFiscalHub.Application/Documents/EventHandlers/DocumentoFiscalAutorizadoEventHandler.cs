using Mediator;
using VisuFiscalHub.Application.Common.Interfaces;
using VisuFiscalHub.Domain.Events;

namespace VisuFiscalHub.Application.Documents.EventHandlers;

internal sealed class DocumentoFiscalAutorizadoEventHandler
    : INotificationHandler<DocumentoFiscalAutorizadoEvent>
{
    private readonly IWebhookDeliveryService _webhookDelivery;

    public DocumentoFiscalAutorizadoEventHandler(IWebhookDeliveryService webhookDelivery)
    {
        _webhookDelivery = webhookDelivery;
    }

    public async ValueTask Handle(
        DocumentoFiscalAutorizadoEvent notification,
        CancellationToken cancellationToken)
    {
        await _webhookDelivery.DeliverAsync(
            notification.DocumentoFiscalId,
            notification.ClienteAppId,
            cancellationToken);
    }
}

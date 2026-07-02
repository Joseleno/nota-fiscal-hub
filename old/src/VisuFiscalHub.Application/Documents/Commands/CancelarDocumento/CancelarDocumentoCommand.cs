using Mediator;
using VisuFiscalHub.Application.Common.Models;
using VisuFiscalHub.Domain.Common;
using VisuFiscalHub.Domain.Identifiers;

namespace VisuFiscalHub.Application.Documents.Commands.CancelarDocumento;

public sealed record CancelarDocumentoCommand : ICommand<Result<CancelarDocumentoResponse>>
{
    public DocumentoFiscalId DocumentoId { get; init; }
    public ClienteAppId ClienteAppId { get; init; }
    public string Justificativa { get; init; } = string.Empty;
}

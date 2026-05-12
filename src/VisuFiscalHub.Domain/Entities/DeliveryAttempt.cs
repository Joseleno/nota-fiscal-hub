using VisuFiscalHub.Domain.Common;
using VisuFiscalHub.Domain.Enums;
using VisuFiscalHub.Domain.Identifiers;

namespace VisuFiscalHub.Domain.Entities;

public sealed class DeliveryAttempt : Entity<DeliveryAttemptId>
{
    private DeliveryAttempt()
    {
        // Para EF Core — inicialização via reflexão
    }

    public DeliveryAttempt(
        DeliveryAttemptId id,
        DocumentoFiscalId documentoFiscalId,
        TipoTentativa tipoTentativa,
        DateTime attemptedAt,
        bool success,
        string? responseCode,
        string? responseMessage,
        long elapsedMs) : base(id)
    {
        DocumentoFiscalId = documentoFiscalId;
        TipoTentativa = tipoTentativa;
        AttemptedAt = attemptedAt;
        Success = success;
        ResponseCode = responseCode;
        ResponseMessage = responseMessage;
        ElapsedMs = elapsedMs;
    }

    public DocumentoFiscalId DocumentoFiscalId { get; private set; }
    public TipoTentativa TipoTentativa { get; private set; }
    public DateTime AttemptedAt { get; private set; }
    public bool Success { get; private set; }
    public string? ResponseCode { get; private set; }
    public string? ResponseMessage { get; private set; }
    public long ElapsedMs { get; private set; }
}

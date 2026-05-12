using VisuFiscalHub.Domain.Entities;

namespace VisuFiscalHub.Application.Common.Interfaces;

public sealed record SefazRetorno(
    bool Autorizado,
    string CStat,
    string XMotivo,
    string? NProt,
    string? XmlAutorizado);

public sealed record SefazConsultaRetorno(
    bool Encontrado,
    bool Autorizado,
    string CStat,
    string? NProt,
    string? XmlProtocolo);

public interface ISefazClient
{
    Task<SefazRetorno> SubmeterAutorizacaoAsync(
        DocumentoFiscal documento,
        Tenant tenant,
        CancellationToken ct);

    Task<SefazConsultaRetorno> ConsultarNfeAsync(
        string chaveAcesso,
        Tenant tenant,
        CancellationToken ct);
}

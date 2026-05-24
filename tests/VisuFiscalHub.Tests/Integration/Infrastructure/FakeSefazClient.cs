using VisuFiscalHub.Application.Common.Interfaces;
using VisuFiscalHub.Domain.Common;
using VisuFiscalHub.Domain.Identifiers;

namespace VisuFiscalHub.Tests.Integration.Infrastructure;

/// <summary>
/// Stub configurável para ISefazClient no ambiente de testes de integração.
/// O comportamento padrão retorna Autorizado — use SimularRejeitado() para cenários de erro.
/// </summary>
public sealed class FakeSefazClient : ISefazClient
{
    private Func<DocumentoFiscalId, TenantId, Task<Result<SefazRetorno>>> _submitHandler =
        (_, _) => Task.FromResult(Result.Success(new SefazRetorno(
            Autorizado: true,
            CStat: "100",
            XMotivo: "Autorizado o uso da NF-e",
            NProt: "135260000000001",
            XmlAutorizado: "<protNFe/>",
            QrCodeUrl: "https://www.sefaz.rs.gov.br/NFCE/NFCE-consulta.aspx?p=fake|2|1|fake")));

    private Func<string, TenantId, Task<Result<SefazConsultaRetorno>>> _consultaHandler =
        (_, _) => Task.FromResult(Result.Success(new SefazConsultaRetorno(
            Encontrado: true,
            Autorizado: true,
            CStat: "100",
            NProt: "135260000000001",
            XmlProtocolo: null)));

    public void SimularAutorizado() =>
        _submitHandler = (_, _) => Task.FromResult(Result.Success(new SefazRetorno(
            Autorizado: true, CStat: "100", XMotivo: "Autorizado o uso da NF-e",
            NProt: "135260000000001", XmlAutorizado: "<protNFe/>",
            QrCodeUrl: "https://www.sefaz.rs.gov.br/NFCE/NFCE-consulta.aspx?p=fake|2|1|fake")));

    public void SimularRejeitado(string cStat = "999", string motivo = "Rejeição simulada") =>
        _submitHandler = (_, _) => Task.FromResult(Result.Success(new SefazRetorno(
            Autorizado: false, CStat: cStat, XMotivo: motivo,
            NProt: null, XmlAutorizado: null, QrCodeUrl: null)));

    public Task<Result<SefazRetorno>> SubmeterAutorizacaoAsync(
        DocumentoFiscalId documentoId, TenantId tenantId, CancellationToken ct) =>
        _submitHandler(documentoId, tenantId);

    public Task<Result<SefazConsultaRetorno>> ConsultarNfeAsync(
        string chaveAcesso, TenantId tenantId, CancellationToken ct) =>
        _consultaHandler(chaveAcesso, tenantId);
}

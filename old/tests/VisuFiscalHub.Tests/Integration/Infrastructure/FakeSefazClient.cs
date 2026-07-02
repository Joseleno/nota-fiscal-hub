using VisuFiscalHub.Application.Common.Interfaces;
using VisuFiscalHub.Domain.Common;
using VisuFiscalHub.Domain.Enums;
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
            QrCodeUrl: "https://www.sefaz.rs.gov.br/NFCE/NFCE-consulta.aspx?p=43260111222333000181650010000000011000000014|2|1|a3f1c2b4d5e6f7890a1b2c3d4e5f6a7b8c9d0e1f",
            ElapsedMs: 0L)));

    private Func<string, TenantId, TipoDocumento, Task<Result<SefazConsultaRetorno>>> _consultaHandler =
        (_, _, _) => Task.FromResult(Result.Success(new SefazConsultaRetorno(
            Encontrado: true,
            Autorizado: true,
            CStat: "100",
            NProt: "135260000000001",
            XmlProtocolo: null,
            ElapsedMs: 0L)));

    public void SimularAutorizado() =>
        _submitHandler = (_, _) => Task.FromResult(Result.Success(new SefazRetorno(
            Autorizado: true, CStat: "100", XMotivo: "Autorizado o uso da NF-e",
            NProt: "135260000000001", XmlAutorizado: "<protNFe/>",
            QrCodeUrl: "https://www.sefaz.rs.gov.br/NFCE/NFCE-consulta.aspx?p=43260111222333000181650010000000011000000014|2|1|a3f1c2b4d5e6f7890a1b2c3d4e5f6a7b8c9d0e1f",
            ElapsedMs: 0L)));

    public void SimularRejeitado(string cStat = "999", string motivo = "Rejeição simulada") =>
        _submitHandler = (_, _) => Task.FromResult(Result.Success(new SefazRetorno(
            Autorizado: false, CStat: cStat, XMotivo: motivo,
            NProt: null, XmlAutorizado: null, QrCodeUrl: null, ElapsedMs: 0L)));

    public void SimularDenegado(string cStat = "110", string motivo = "Uso Denegado") =>
        _submitHandler = (_, _) => Task.FromResult(Result.Success(new SefazRetorno(
            Autorizado: false, CStat: cStat, XMotivo: motivo,
            NProt: null, XmlAutorizado: null, QrCodeUrl: null, ElapsedMs: 0L)));

    public void SimularDuplicidade(string cStat = "204", string nProtConsulta = "135260000000001")
    {
        _submitHandler = (_, _) => Task.FromResult(Result.Success(new SefazRetorno(
            Autorizado: false, CStat: cStat, XMotivo: "NF-e em duplicidade",
            NProt: null, XmlAutorizado: null, QrCodeUrl: null, ElapsedMs: 0L)));

        _consultaHandler = (_, _, _) => Task.FromResult(Result.Success(new SefazConsultaRetorno(
            Encontrado: true, Autorizado: true, CStat: "100",
            NProt: nProtConsulta, XmlProtocolo: "<protNFe/>", ElapsedMs: 0L)));
    }

    public Task<Result<SefazRetorno>> SubmeterAutorizacaoAsync(
        DocumentoFiscalId documentoId, TenantId tenantId, CancellationToken ct) =>
        _submitHandler(documentoId, tenantId);

    public Task<Result<SefazConsultaRetorno>> ConsultarNfeAsync(
        string chaveAcesso, TenantId tenantId, TipoDocumento tipo, CancellationToken ct) =>
        _consultaHandler(chaveAcesso, tenantId, tipo);
}

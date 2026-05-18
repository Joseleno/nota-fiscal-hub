using System.Xml;
using Microsoft.Extensions.Logging;
using VisuFiscalHub.Application.Common.Interfaces;
using VisuFiscalHub.Domain.Common;
using VisuFiscalHub.Domain.Identifiers;
using VisuFiscalHub.Domain.Interfaces;

namespace VisuFiscalHub.Infrastructure.Fiscal.Sefaz;

/// <summary>
/// Implementação real de ISefazClient para NFC-e.
/// Orquestra: carregar documento → carregar tenant → certificado mTLS → build XML → assinar → enviar → parsear.
/// </summary>
internal sealed class SefazClient : ISefazClient
{
    private readonly IDocumentoFiscalRepository _documentoRepo;
    private readonly ITenantRepository _tenantRepo;
    private readonly ITenantCertificateProvider _certProvider;
    private readonly INfceXmlBuilder _xmlBuilder;
    private readonly XmlSigner _signer;
    private readonly SefazHttpClient _httpClient;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<SefazClient> _logger;

    public SefazClient(
        IDocumentoFiscalRepository documentoRepo,
        ITenantRepository tenantRepo,
        ITenantCertificateProvider certProvider,
        INfceXmlBuilder xmlBuilder,
        XmlSigner signer,
        SefazHttpClient httpClient,
        TimeProvider timeProvider,
        ILogger<SefazClient> logger)
    {
        _documentoRepo = documentoRepo;
        _tenantRepo = tenantRepo;
        _certProvider = certProvider;
        _xmlBuilder = xmlBuilder;
        _signer = signer;
        _httpClient = httpClient;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    public async Task<Result<SefazRetorno>> SubmeterAutorizacaoAsync(
        DocumentoFiscalId documentoId,
        TenantId tenantId,
        CancellationToken ct)
    {
        var documento = await _documentoRepo.GetByIdAsync(documentoId, ct);
        if (documento is null)
            return Falha("Documento não encontrado.", documentoId);

        var tenant = await _tenantRepo.GetByIdAsync(tenantId, ct);
        if (tenant is null || !tenant.IsActive)
            return Falha("Tenant não encontrado ou inativo.", documentoId);

        var certResult = await _certProvider.GetCertificateAsync(tenantId, ct);
        if (certResult.IsFailure)
        {
            _logger.LogWarning("Certificado indisponível para Tenant {TenantId}: {Error}",
                tenantId.Value, certResult.Error.Code);
            return Result.Failure<SefazRetorno>(certResult.Error);
        }

        using var certificate = certResult.Value;

        var xmlResult = _xmlBuilder.Construir(documento, tenant);
        if (xmlResult.IsFailure)
        {
            _logger.LogError("Falha ao construir XML para documento {DocumentoId}: {Error}",
                documentoId.Value, xmlResult.Error.Code);
            return Result.Failure<SefazRetorno>(xmlResult.Error);
        }

        XmlDocument xmlAssinado;
        try
        {
            xmlAssinado = _signer.Assinar(
                xmlResult.Value,
                certificate,
                documento.ChaveAcesso.Valor);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Falha ao assinar XML do documento {DocumentoId}", documentoId.Value);
            return Result.Failure<SefazRetorno>(
                new Error("Sefaz.AssinaturaFalhou", "Falha ao assinar o XML NFC-e."));
        }

        var xmlStr   = xmlAssinado.OuterXml;
        var ufCodigo = tenant.ConfiguracaoFiscal.UfCodigo;
        var tpAmb    = (int)tenant.ConfiguracaoFiscal.Ambiente;
        var url      = SefazEndpointResolver.Autorizacao(ufCodigo, tenant.ConfiguracaoFiscal.Ambiente);
        var idLote   = _timeProvider.GetUtcNow().ToString("yyyyMMddHHmmss") + "0";
        var envelope = SoapEnvelopeBuilder.BuildAutorizacao(xmlStr, ufCodigo, tpAmb, idLote);

        string soapResponse;
        try
        {
            soapResponse = await _httpClient.PostSoapAsync(url, envelope, certificate, ct);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or TimeoutException)
        {
            _logger.LogWarning(ex, "Falha ao enviar NFC-e {DocumentoId} para SEFAZ {Url}",
                documentoId.Value, url);
            return Result.Failure<SefazRetorno>(
                new Error("Sefaz.HttpFalhou", $"Falha na comunicação com SEFAZ: {ex.Message}"));
        }

        return SefazRetornoParser.Parse(soapResponse);
    }

    public async Task<Result<SefazConsultaRetorno>> ConsultarNfeAsync(
        string chaveAcesso,
        TenantId tenantId,
        CancellationToken ct)
    {
        // Consulta de NFC-e utiliza o mesmo endpoint de autorização (serviço NFeConsultaProtocolo4).
        // Implementação básica: parse do status do documento via retorno da consulta.
        var tenant = await _tenantRepo.GetByIdAsync(tenantId, ct);
        if (tenant is null || !tenant.IsActive)
            return Result.Failure<SefazConsultaRetorno>(
                new Error("Sefaz.TenantInvalido", "Tenant não encontrado."));

        var certResult = await _certProvider.GetCertificateAsync(tenantId, ct);
        if (certResult.IsFailure)
            return Result.Failure<SefazConsultaRetorno>(certResult.Error);

        using var certificate = certResult.Value;

        var ufCodigo = tenant.ConfiguracaoFiscal.UfCodigo;
        var tpAmb    = (int)tenant.ConfiguracaoFiscal.Ambiente;

        // URL de consulta mapeada explicitamente — nunca derivada por string.Replace.
        var url      = SefazEndpointResolver.ConsultaProtocolo(ufCodigo, tenant.ConfiguracaoFiscal.Ambiente);
        var envelope = BuildConsultaEnvelope(chaveAcesso, ufCodigo, tpAmb);

        string soapResponse;
        try
        {
            soapResponse = await _httpClient.PostSoapAsync(url, envelope, certificate, ct);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or TimeoutException)
        {
            _logger.LogWarning(ex, "Falha ao consultar NFC-e {Chave} na SEFAZ {Url}", chaveAcesso, url);
            return Result.Failure<SefazConsultaRetorno>(
                new Error("Sefaz.ConsultaFalhou", $"Falha na consulta SEFAZ: {ex.Message}"));
        }

        return ParseConsultaResponse(soapResponse);
    }

    private static string BuildConsultaEnvelope(string chaveAcesso, int cUF, int tpAmb)
    {
        // Namespace do wsdl de consulta: NFeConsultaProtocolo4 (diferente do de autorização NFeAutorizacao4).
        const string WsConsultaNs = "http://www.portalfiscal.inf.br/nfe/wsdl/NFeConsultaProtocolo4";
        return $"""
            <soap12:Envelope xmlns:soap12="http://www.w3.org/2003/05/soap-envelope">
              <soap12:Header>
                <nfeCabMsg xmlns="{WsConsultaNs}">
                  <cUF xmlns="{WsConsultaNs}">{cUF}</cUF>
                  <versaoDados xmlns="{WsConsultaNs}">4.01</versaoDados>
                </nfeCabMsg>
              </soap12:Header>
              <soap12:Body>
                <nfeDadosMsg xmlns="{WsConsultaNs}">
                  <consSitNFe versao="4.01" xmlns="http://www.portalfiscal.inf.br/nfe">
                    <tpAmb>{tpAmb}</tpAmb>
                    <xServ>CONSULTAR</xServ>
                    <chNFe>{chaveAcesso}</chNFe>
                  </consSitNFe>
                </nfeDadosMsg>
              </soap12:Body>
            </soap12:Envelope>
            """;
    }

    private static Result<SefazConsultaRetorno> ParseConsultaResponse(string soapResponse)
    {
        try
        {
            var doc = new XmlDocument();
            doc.LoadXml(soapResponse);

            var ns = new XmlNamespaceManager(doc.NameTable);
            ns.AddNamespace("nfe", "http://www.portalfiscal.inf.br/nfe");

            var retNode = doc.SelectSingleNode("//nfe:retConsSitNFe", ns);
            if (retNode is null)
                return Result.Failure<SefazConsultaRetorno>(
                    new Error("Sefaz.ConsultaRetornoInvalido", "Resposta de consulta inválida."));

            var cStat   = retNode.SelectSingleNode("nfe:cStat", ns)?.InnerText ?? string.Empty;
            var nProt   = retNode.SelectSingleNode(".//nfe:nProt", ns)?.InnerText;
            var xmlProt = retNode.SelectSingleNode(".//nfe:protNFe", ns)?.OuterXml;

            // Semântica idêntica à de SefazRetornoParser: apenas cStat=100 é Autorizado.
            // cStat=204/572 = duplicidade na consulta — Encontrado=true, Autorizado=false.
            // ReconciliacaoJobProcessor trata Encontrado+!Autorizado como rejeição definitiva.
            var autorizado = cStat == "100";
            var encontrado = !SefazRetornoParser.IsNaoEncontrado(cStat);

            return Result.Success(new SefazConsultaRetorno(
                Encontrado: encontrado,
                Autorizado: autorizado,
                CStat:      cStat,
                NProt:      nProt,
                XmlProtocolo: xmlProt));
        }
        catch (XmlException ex)
        {
            return Result.Failure<SefazConsultaRetorno>(
                new Error("Sefaz.ConsultaXmlInvalido", $"Falha ao parsear consulta SEFAZ: {ex.Message}"));
        }
    }

    private Result<SefazRetorno> Falha(string motivo, DocumentoFiscalId id)
    {
        _logger.LogError("SubmeterAutorizacao falhou para {DocumentoId}: {Motivo}", id.Value, motivo);
        return Result.Failure<SefazRetorno>(new Error("Sefaz.PreCondicaoFalhou", motivo));
    }
}

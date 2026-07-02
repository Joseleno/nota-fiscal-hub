using Microsoft.Extensions.Logging;
using VisuFiscalHub.Application.Common.Interfaces;
using VisuFiscalHub.Domain.Common;
using VisuFiscalHub.Domain.Identifiers;
using VisuFiscalHub.Domain.Interfaces;

namespace VisuFiscalHub.Infrastructure.Fiscal.Prefeitura;

/// <summary>
/// Implementação real de IPrefeituraClient para NFS-e ABRASF v2.04.
/// Orquestra: carregar documento → carregar tenant → build RPS XML → enviar → parsear.
/// Assinatura digital do RPS via certificado A1/A3 será adicionada na Fase 16.
/// </summary>
internal sealed class PrefeituraClient : IPrefeituraClient
{
    private readonly IDocumentoFiscalRepository _documentoRepo;
    private readonly ITenantRepository _tenantRepo;
    private readonly INfseXmlBuilder _xmlBuilder;
    private readonly PrefeituraHttpClient _httpClient;
    private readonly ILogger<PrefeituraClient> _logger;

    public PrefeituraClient(
        IDocumentoFiscalRepository documentoRepo,
        ITenantRepository tenantRepo,
        INfseXmlBuilder xmlBuilder,
        PrefeituraHttpClient httpClient,
        ILogger<PrefeituraClient> logger)
    {
        _documentoRepo = documentoRepo;
        _tenantRepo = tenantRepo;
        _xmlBuilder = xmlBuilder;
        _httpClient = httpClient;
        _logger = logger;
    }

    public async Task<Result<PrefeituraRetorno>> EnviarRpsAsync(
        DocumentoFiscalId documentoId,
        TenantId tenantId,
        CancellationToken ct)
    {
        var documento = await _documentoRepo.GetByIdAsync(documentoId, ct);
        if (documento is null)
        {
            _logger.LogError("Documento NFS-e {DocumentoId} não encontrado", documentoId.Value);
            return Result.Failure<PrefeituraRetorno>(
                new Error("Prefeitura.DocumentoNaoEncontrado", "Documento não encontrado."));
        }

        var tenant = await _tenantRepo.GetByIdAsync(tenantId, ct);
        if (tenant is null || !tenant.IsActive)
        {
            _logger.LogError("Tenant {TenantId} não encontrado ou inativo para NFS-e", tenantId.Value);
            return Result.Failure<PrefeituraRetorno>(
                new Error("Prefeitura.TenantInvalido", "Tenant não encontrado ou inativo."));
        }

        var codigoMunicipio = tenant.Endereco.CodigoMunicipio;
        var url = PrefeituraEndpointResolver.ResolverGerarNfse(codigoMunicipio, tenant.ConfiguracaoFiscal.Ambiente);
        if (url is null)
        {
            _logger.LogError(
                "Município {CodigoMunicipio} não possui endpoint ABRASF configurado para Tenant {TenantId}",
                codigoMunicipio, tenantId.Value);
            return Result.Failure<PrefeituraRetorno>(
                new Error("Prefeitura.MunicipioNaoSuportado",
                    $"Município {codigoMunicipio} não possui webservice ABRASF configurado."));
        }

        var xmlResult = _xmlBuilder.ConstruirRps(documento, tenant);
        if (xmlResult.IsFailure)
        {
            _logger.LogError("Falha ao construir RPS para NFS-e {DocumentoId}: {Error}",
                documentoId.Value, xmlResult.Error.Code);
            return Result.Failure<PrefeituraRetorno>(xmlResult.Error);
        }

        // Fase 16 adicionará assinatura digital do RPS via certificado A1/A3 do tenant.
        var soapEnvelope = BuildGerarNfseEnvelope(xmlResult.Value.OuterXml);

        var sw = System.Diagnostics.Stopwatch.StartNew();
        string soapResponse;
        try
        {
            soapResponse = await _httpClient.PostSoapAsync(url, soapEnvelope, ct);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or TimeoutException)
        {
            _logger.LogWarning(ex, "Falha ao enviar NFS-e {DocumentoId} para {Url}",
                documentoId.Value, url);
            return Result.Failure<PrefeituraRetorno>(
                new Error("Prefeitura.HttpFalhou", $"Falha na comunicação com a prefeitura: {ex.Message}"));
        }

        sw.Stop();
        var retorno = PrefeituraRetornoParser.ParseGerarNfse(soapResponse);
        if (retorno.IsFailure) return retorno;
        return Result.Success(retorno.Value with { ElapsedMs = sw.ElapsedMilliseconds });
    }

    public async Task<Result<PrefeituraConsultaRetorno>> ConsultarNfseAsync(
        string numeroRps,
        string serieRps,
        TenantId tenantId,
        int codigoMunicipio,
        CancellationToken ct)
    {
        var tenant = await _tenantRepo.GetByIdAsync(tenantId, ct);
        if (tenant is null || !tenant.IsActive)
            return Result.Failure<PrefeituraConsultaRetorno>(
                new Error("Prefeitura.TenantInvalido", "Tenant não encontrado."));

        var url = PrefeituraEndpointResolver.ResolverConsultarNfse(codigoMunicipio, tenant.ConfiguracaoFiscal.Ambiente);
        if (url is null)
            return Result.Failure<PrefeituraConsultaRetorno>(
                new Error("Prefeitura.MunicipioNaoSuportado",
                    $"Município {codigoMunicipio} não possui webservice ABRASF configurado."));

        if (string.IsNullOrWhiteSpace(tenant.ConfiguracaoFiscal.InscricaoMunicipal))
            return Result.Failure<PrefeituraConsultaRetorno>(
                new Error("Prefeitura.InscricaoMunicipalAusente",
                    "InscricaoMunicipal é obrigatória para consulta NFS-e."));

        var envelope = BuildConsultaEnvelope(
            numeroRps, serieRps,
            tenant.Cnpj.Valor,
            tenant.ConfiguracaoFiscal.InscricaoMunicipal!,
            codigoMunicipio);

        var sw = System.Diagnostics.Stopwatch.StartNew();
        string soapResponse;
        try
        {
            soapResponse = await _httpClient.PostSoapAsync(url, envelope, ct);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or TimeoutException)
        {
            _logger.LogWarning(ex, "Falha ao consultar NFS-e RPS {Numero}/{Serie} na prefeitura {Url}",
                numeroRps, serieRps, url);
            return Result.Failure<PrefeituraConsultaRetorno>(
                new Error("Prefeitura.ConsultaFalhou", $"Falha na consulta à prefeitura: {ex.Message}"));
        }

        sw.Stop();
        var parseResult = PrefeituraRetornoParser.ParseConsultarNfse(soapResponse);
        if (parseResult.IsFailure) return parseResult;
        return Result.Success(parseResult.Value with { ElapsedMs = sw.ElapsedMilliseconds });
    }

    private static string BuildGerarNfseEnvelope(string rpsXml)
    {
        const string NfseWsNs = "http://www.abrasf.org.br/nfse.xsd";
        return $"""
            <soap12:Envelope xmlns:soap12="http://www.w3.org/2003/05/soap-envelope">
              <soap12:Body>
                <RecepcionarLoteRps xmlns="{NfseWsNs}">
                  <nfseDadosMsg>
                    {rpsXml}
                  </nfseDadosMsg>
                </RecepcionarLoteRps>
              </soap12:Body>
            </soap12:Envelope>
            """;
    }

    private static string BuildConsultaEnvelope(string numeroRps, string serieRps, string cnpj,
        string inscricaoMunicipal, int codigoMunicipio)
    {
        const string NfseWsNs = "http://www.abrasf.org.br/nfse.xsd";
        return $"""
            <soap12:Envelope xmlns:soap12="http://www.w3.org/2003/05/soap-envelope">
              <soap12:Body>
                <ConsultarNfsePorRps xmlns="{NfseWsNs}">
                  <nfseDadosMsg>
                    <ConsultarNfsePorRpsEnvio versao="2.04" xmlns="{NfseWsNs}">
                      <IdentificacaoRps>
                        <Numero>{numeroRps}</Numero>
                        <Serie>{serieRps}</Serie>
                        <Tipo>1</Tipo>
                      </IdentificacaoRps>
                      <Prestador>
                        <CpfCnpj>
                          <Cnpj>{cnpj}</Cnpj>
                        </CpfCnpj>
                        <InscricaoMunicipal>{inscricaoMunicipal}</InscricaoMunicipal>
                      </Prestador>
                    </ConsultarNfsePorRpsEnvio>
                  </nfseDadosMsg>
                </ConsultarNfsePorRps>
              </soap12:Body>
            </soap12:Envelope>
            """;
    }
}

// Stubs pendentes de implementação real. Lançam NotImplementedException em runtime (não em startup).
// CertificateEncryptionService e TokenService foram promovidos a implementações reais — stubs removidos.
using System.Security.Cryptography.X509Certificates;
using System.Xml;
using VisuFiscalHub.Application.Common.Interfaces;
using VisuFiscalHub.Domain.Common;
using VisuFiscalHub.Domain.Enums;
using VisuFiscalHub.Domain.Identifiers;
using VisuFiscalHub.Domain.ValueObjects;

namespace VisuFiscalHub.Infrastructure.Services.Stubs;

internal sealed class TenantCertificateProviderStub : ITenantCertificateProvider
{
    public Task<Result<X509Certificate2>> GetCertificateAsync(TenantId tenantId, CancellationToken ct = default)
        => throw new NotImplementedException("ITenantCertificateProvider não implementado.");
}

internal sealed class SefazClientStub : ISefazClient
{
    public Task<Result<SefazRetorno>> SubmeterAutorizacaoAsync(DocumentoFiscalId documentoId, TenantId tenantId, CancellationToken ct)
        => throw new NotImplementedException("ISefazClient não implementado.");

    public Task<Result<SefazConsultaRetorno>> ConsultarNfeAsync(string chaveAcesso, TenantId tenantId, CancellationToken ct)
        => throw new NotImplementedException("ISefazClient não implementado.");
}

internal sealed class NfceXmlBuilderStub : INfceXmlBuilder
{
    public Result<XmlDocument> Construir(VisuFiscalHub.Domain.Entities.DocumentoFiscal documento, VisuFiscalHub.Domain.Entities.Tenant tenant)
        => throw new NotImplementedException("INfceXmlBuilder não implementado.");
}

internal sealed class QrCodeGeneratorStub : IQrCodeGenerator
{
    public Result<QrCode> Gerar(ChaveAcesso chaveAcesso, AmbienteSefaz ambiente, string csc, string urlConsultaSefaz)
        => throw new NotImplementedException("IQrCodeGenerator não implementado.");
}

internal sealed class TributacaoCalculatorStub : ITributacaoCalculator
{
    public Result<Tributo> CalcularParaCrt1(Produto produto, CSOSN csosn = CSOSN.Csosn400)
        => throw new NotImplementedException("ITributacaoCalculator não implementado.");

    public Result<Tributo> CalcularParaCrt2(Produto produto, decimal aliquotaIcms, decimal aliquotaPis, decimal aliquotaCofins)
        => throw new NotImplementedException("ITributacaoCalculator não implementado.");

    public Result<Tributo> CalcularParaCrt3(Produto produto, decimal aliquotaIcms, decimal aliquotaPis, decimal aliquotaCofins)
        => throw new NotImplementedException("ITributacaoCalculator não implementado.");
}

internal sealed class WebhookDeliveryServiceStub : IWebhookDeliveryService
{
    public Task DeliverAsync(DocumentoFiscalId documentoId, ClienteAppId clienteAppId, CancellationToken ct)
        => throw new NotImplementedException("IWebhookDeliveryService não implementado.");
}

internal sealed class DocumentJobQueueStub : IDocumentJobQueue
{
    public Task EnqueueProcessingAsync(DocumentoFiscalId id, CancellationToken ct = default)
        => throw new NotImplementedException("IDocumentJobQueue não implementado.");
}

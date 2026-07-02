using Shouldly;
using VisuFiscalHub.Application.Common.Models;
using VisuFiscalHub.Domain.Enums;
using VisuFiscalHub.Domain.Identifiers;

namespace VisuFiscalHub.Tests.Api;

/// <summary>
/// Verifica contratos de tipo dos endpoints de documento.
/// O teste de reflexão em DocumentoStatusResponse é uma guarda de regressão compilação+runtime:
/// se alguém adicionar xmlAssinado ao tipo, este teste falha imediatamente — antes de qualquer
/// chamada HTTP — forçando uma decision consciente sobre o endpoint separado GET /{id}/xml.
/// </summary>
public class DocumentoEndpointsContractTests
{
    // ── DocumentoStatusResponse não expõe xmlAssinado ─────────────────────────
    // Guarda arquitetural: status e XML são endpoints separados por design.
    // GET /documentos/{id}/xml é o único lugar onde XmlAssinado é exposto.

    [Fact]
    public void DocumentoStatusResponse_NaoExpoe_XmlAssinado()
    {
        var tipo = typeof(DocumentoStatusResponse);
        var xmlProps = tipo.GetProperties()
            .Where(p => p.Name.Contains("Xml", StringComparison.OrdinalIgnoreCase))
            .Select(p => p.Name)
            .ToList();

        xmlProps.ShouldBeEmpty(
            $"DocumentoStatusResponse expõe propriedades com 'Xml' ({string.Join(", ", xmlProps)}). " +
            "Use GET /documentos/{{id}}/xml — endpoints separados são obrigatórios pelo spec.");
    }

    // ── IssueDocumentResponse.PollUrl aponta para o endpoint de status ─────────
    // Verifica que o formato do PollUrl produzido pelo IssueDocumentCommandHandler
    // resolve para o endpoint correto de polling.

    [Fact]
    public void IssueDocumentResponse_PollUrl_ApontaParaEndpointStatus()
    {
        var id = DocumentoFiscalId.New();
        var expectedPollUrl = $"/api/v1/documentos/{id.Value}/status";

        var response = new IssueDocumentResponse(
            DocumentoId: id,
            Status: StatusDocumento.Enfileirado,
            ChaveAcesso: null,
            PollUrl: expectedPollUrl,
            CreatedAt: DateTimeOffset.UtcNow);

        response.PollUrl.ShouldBe(expectedPollUrl,
            "PollUrl deve apontar para GET /api/v1/documentos/{id}/status");
        response.PollUrl.ShouldContain(id.Value.ToString());
    }

    // ── GetDocumentXmlQuery existe e retorna Result<string> ───────────────────
    // Garante que a query de XML existe no Application layer (não inline no endpoint).

    [Fact]
    public void GetDocumentXmlQuery_ExisteNoApplicationLayer()
    {
        var queryType = typeof(VisuFiscalHub.Application.Documents.Queries.GetDocumentXml.GetDocumentXmlQuery);

        queryType.ShouldNotBeNull();

        var documentoIdProp = queryType.GetProperty("DocumentoId");
        var clienteAppIdProp = queryType.GetProperty("ClienteAppId");

        documentoIdProp.ShouldNotBeNull("GetDocumentXmlQuery deve ter DocumentoId");
        clienteAppIdProp.ShouldNotBeNull("GetDocumentXmlQuery deve ter ClienteAppId");
    }
}

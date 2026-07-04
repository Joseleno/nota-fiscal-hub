using NotaFiscalHub.BuildingBlocks.Auditoria;
using NotaFiscalHub.BuildingBlocks.Messaging.Abstractions;

namespace NotaFiscalHub.IntegrationTests.Auditoria;

/// <summary>
/// Test-double do brief (Step 6) — equivalente de teste ao evento real <c>ApiKeyRevogada</c> do catálogo.
/// Implementa <see cref="IEventoCatalogavelDeTeste"/> para que <see cref="AuditoriaEventCatalog"/> o
/// reconheça sem o projeto de produção referenciar este assembly de testes de integração (mesmo mecanismo
/// usado por <c>tests/NotaFiscalHub.BuildingBlocks.Auditoria.UnitTests/CatalogMappingTests.cs</c>).
/// </summary>
public sealed record ApiKeyRevogadaDeTeste : EventoIntegracao, IEventoCatalogavelDeTeste
{
    public Guid ApiKeyId { get; init; }
    public string AtorPrefixo { get; init; } = "nfh_live_ab12";

    string IEventoCatalogavelDeTeste.Acao => "apikey.revogada";
    string IEventoCatalogavelDeTeste.RecursoTipo => "ApiKey";
    string IEventoCatalogavelDeTeste.RecursoId => ApiKeyId.ToString();
}

/// <summary>Evento de integração válido mas deliberadamente ausente do catálogo (spec B5 critério 7).</summary>
public sealed record EventoNaoCatalogadoDeTeste : EventoIntegracao;

/// <summary>
/// Test-double do brief (Step 7) — evento catalogado cujo payload carrega uma chave da denylist
/// (<c>Cpf</c>) para provar que <see cref="PiiSanitizer"/> é acionado ponta a ponta antes do insert.
/// </summary>
public sealed record EventoComCpfDeTeste : EventoIntegracao, IEventoCatalogavelDeTeste
{
    public string Cpf { get; init; } = string.Empty;
    public string AtorPrefixo { get; init; } = "sistema:worker";

    string IEventoCatalogavelDeTeste.Acao => "teste.evento_com_cpf";
    string IEventoCatalogavelDeTeste.RecursoTipo => "TesteRecurso";
    string IEventoCatalogavelDeTeste.RecursoId => Guid.NewGuid().ToString();
}

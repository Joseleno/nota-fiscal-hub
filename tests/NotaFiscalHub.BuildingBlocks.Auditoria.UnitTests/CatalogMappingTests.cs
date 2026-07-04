using NotaFiscalHub.BuildingBlocks.Auditoria;
using NotaFiscalHub.BuildingBlocks.Messaging.Abstractions;
using Xunit;

namespace NotaFiscalHub.BuildingBlocks.Auditoria.UnitTests;

/// <summary>
/// Spec B5 §Abordagem passo 2: catálogo mapeia evento conhecido → registro com todos os campos;
/// evento desconhecido → ignorado (retorna <see langword="false"/>, nunca lança).
/// </summary>
public class CatalogMappingTests
{
    [Fact]
    public void TryMap_EventoConhecido_RetornaRegistroComTodosOsCampos()
    {
        var contaId = Guid.NewGuid();
        var catalogo = new AuditoriaEventCatalog();

        var sucesso = catalogo.TryMap(
            new ApiKeyRevogadaDeTeste { ContaId = contaId, ApiKeyId = Guid.NewGuid(), AtorPrefixo = "nfh_live_ab12" },
            out var registro);

        Assert.True(sucesso);
        Assert.Equal("apikey.revogada", registro.Acao);
        Assert.Equal(contaId, registro.ContaId);
        Assert.Equal("ApiKey", registro.RecursoTipo);
        Assert.False(string.IsNullOrWhiteSpace(registro.RecursoId));
        Assert.False(string.IsNullOrWhiteSpace(registro.Ator));
        Assert.False(string.IsNullOrWhiteSpace(registro.TipoEvento));
    }

    [Fact]
    public void TryMap_EventoDesconhecido_RetornaFalse()
    {
        var catalogo = new AuditoriaEventCatalog();

        var sucesso = catalogo.TryMap(new EventoNaoCatalogadoDeTeste(), out _);

        Assert.False(sucesso);
    }

    [Fact]
    public void TryMap_TodosOsSeisEventosDoCatalogo_SaoReconhecidos()
    {
        // Mantém a suíte honesta quanto ao inventário do catálogo (spec B5: ContaCriada, ApiKeyRotacionada,
        // ApiKeyRevogada, WebhookConfigAlterada, PlanoAlterado, ContaSuspensa).
        var catalogo = new AuditoriaEventCatalog();
        var contaId = Guid.NewGuid();

        Assert.True(catalogo.TryMap(new ContaCriada { ContaId = contaId, CriadaContaId = contaId, AtorPrefixo = "sistema" }, out _));
        Assert.True(catalogo.TryMap(new ApiKeyRotacionada { ContaId = contaId, ApiKeyId = Guid.NewGuid(), AtorPrefixo = "nfh_live_ab12" }, out _));
        Assert.True(catalogo.TryMap(new ApiKeyRevogada { ContaId = contaId, ApiKeyId = Guid.NewGuid(), AtorPrefixo = "nfh_live_ab12" }, out _));
        Assert.True(catalogo.TryMap(new WebhookConfigAlterada { ContaId = contaId, WebhookConfigId = Guid.NewGuid(), AtorPrefixo = "portal:usr1" }, out _));
        Assert.True(catalogo.TryMap(new PlanoAlterado { ContaId = contaId, PlanoId = Guid.NewGuid(), AtorPrefixo = "portal:usr1" }, out _));
        Assert.True(catalogo.TryMap(new ContaSuspensa { ContaId = contaId, AtorPrefixo = "sistema:worker" }, out _));
    }
}

/// <summary>
/// Test-double do brief (Step 2) — representa, para fins de teste, um dos 6 eventos reais do catálogo
/// (equivalente a <see cref="ApiKeyRevogada"/>). Mantido como tipo SEPARADO do evento sintético real
/// porque o brief o define explicitamente por esse nome; implementa <see cref="IEventoCatalogavelDeTeste"/>
/// para que <see cref="AuditoriaEventCatalog"/> o reconheça sem o projeto de produção referenciar este
/// assembly de testes.
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

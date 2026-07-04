using NotaFiscalHub.BuildingBlocks.Auditoria.Contracts;
using NotaFiscalHub.BuildingBlocks.Messaging.Abstractions;

namespace NotaFiscalHub.BuildingBlocks.Auditoria;

/// <summary>
/// Catálogo OPT-IN fechado por tipo CLR (spec B5 §Abordagem passo 1/6). Evento fora desta lista explícita
/// é ignorado por <see cref="TryMap"/> (retorna <see langword="false"/>) — NUNCA lança, e o handler que a
/// consome (<c>AuditoriaEventHandler</c>) não gera registro nem falha o processamento do inbox (critério
/// de aceite 7).
///
/// Reconhece exatamente os 6 eventos sintéticos desta fundação (<see cref="ContaCriada"/>,
/// <see cref="ApiKeyRotacionada"/>, <see cref="ApiKeyRevogada"/>, <see cref="WebhookConfigAlterada"/>,
/// <see cref="PlanoAlterado"/>, <see cref="ContaSuspensa"/>) via <see cref="IEventoCatalogavelDeTeste"/> —
/// interface de marcação que os test-doubles do brief da Tarefa 5 (ex.: <c>ApiKeyRevogadaDeTeste</c>,
/// definido no projeto de testes) implementam para serem tratados como equivalentes ao evento real
/// correspondente sem o catálogo (produção) referenciar o assembly de testes.
/// </summary>
public sealed class AuditoriaEventCatalog : IAuditoriaEventCatalog
{
    public bool TryMap(EventoIntegracao evento, out RegistroAuditoriaNovo registro)
    {
        ArgumentNullException.ThrowIfNull(evento);

        switch (evento)
        {
            case ContaCriada e:
                registro = Mapear(e, "conta.criada", "Conta", e.CriadaContaId.ToString(), AtorDe(e.AtorPrefixo));
                return true;

            case ApiKeyRotacionada e:
                registro = Mapear(e, "apikey.rotacionada", "ApiKey", e.ApiKeyId.ToString(), AtorDe(e.AtorPrefixo));
                return true;

            case ApiKeyRevogada e:
                registro = Mapear(e, "apikey.revogada", "ApiKey", e.ApiKeyId.ToString(), AtorDe(e.AtorPrefixo));
                return true;

            case WebhookConfigAlterada e:
                registro = Mapear(e, "webhook_config.alterada", "WebhookConfig", e.WebhookConfigId.ToString(), AtorDe(e.AtorPrefixo));
                return true;

            case PlanoAlterado e:
                registro = Mapear(e, "plano.alterada", "Plano", e.PlanoId.ToString(), AtorDe(e.AtorPrefixo));
                return true;

            case ContaSuspensa e:
                registro = Mapear(e, "conta.suspensa", "Conta", e.ContaId?.ToString() ?? string.Empty, AtorDe(e.AtorPrefixo));
                return true;

            // Test-double do brief (Step 2, ex.: ApiKeyRevogadaDeTeste) — equivalente de teste a um dos 6
            // eventos reais acima. Ver IEventoCatalogavelDeTeste e
            // tests/NotaFiscalHub.BuildingBlocks.Auditoria.UnitTests/CatalogMappingTests.cs.
            case IEventoCatalogavelDeTeste e:
                registro = Mapear(evento, e.Acao, e.RecursoTipo, e.RecursoId, AtorDe(e.AtorPrefixo));
                return true;

            default:
                registro = null!;
                return false;
        }
    }

    private static RegistroAuditoriaNovo Mapear(
        EventoIntegracao evento, string acao, string recursoTipo, string recursoId, string ator) =>
        new(
            ContaId: evento.ContaId,
            TipoEvento: $"{evento.GetType().FullName}.v1",
            Acao: acao,
            RecursoTipo: recursoTipo,
            RecursoId: recursoId,
            Ator: ator,
            OcorridoEm: evento.OcorridoEm,
            DadosJson: null);

    /// <summary>"Quem" nunca carrega o segredo completo — só o prefixo curto já fornecido pelo evento.</summary>
    private static string AtorDe(string atorPrefixo) => atorPrefixo;
}

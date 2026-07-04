namespace NotaFiscalHub.BuildingBlocks.Auditoria;

/// <summary>
/// Interface de marcação usada exclusivamente por test-doubles de evento de integração (definidos em
/// projetos de teste — ex.: <c>ApiKeyRevogadaDeTeste</c> em
/// <c>tests/NotaFiscalHub.BuildingBlocks.Auditoria.UnitTests</c> e
/// <c>tests/NotaFiscalHub.IntegrationTests/Auditoria</c>) que representam, para fins de teste, um dos 6
/// eventos reais e sintéticos do catálogo (spec B5 §Dependências: "a fundação testa com eventos sintéticos
/// se ainda não existirem"). Permite que <see cref="AuditoriaEventCatalog"/> reconheça esses doubles sem o
/// projeto de PRODUÇÃO referenciar nenhum assembly de teste — a implementação concreta do double é quem
/// decide <see cref="Acao"/>/<see cref="RecursoTipo"/>/<see cref="RecursoId"/>/<see cref="AtorPrefixo"/>.
/// </summary>
public interface IEventoCatalogavelDeTeste
{
    string Acao { get; }
    string RecursoTipo { get; }
    string RecursoId { get; }
    string AtorPrefixo { get; }
}

using NetArchTest.Rules;
using NotaFiscalHub.BuildingBlocks.Messaging.Abstractions;

namespace NotaFiscalHub.ArchitectureTests;

/// <summary>
/// Critério de aceite 6 (spec B3) — payload de evento de integração só carrega identificadores/status.
/// <see cref="EventPayloadRules.Validar"/> é usada aqui com fixtures locais que representam os dois lados
/// da regra: <see cref="EventoComCertificadoId"/> (Guid terminado em "Id" — válvula (a) da denylist,
/// exatamente o payload previsto para <c>CertificadoProximoDoVencimento</c>/<c>CertificadoExpirado</c> na
/// Fase 2) passa; <see cref="EventoComXmlAutorizado"/> (string cujo nome casa com a denylist) falha.
/// </summary>
public class EventPayloadRulesTests
{
    [Fact]
    public void EventoIntegracao_CertificadoId_Passa_XmlAutorizado_Falha()
    {
        var resultado = EventPayloadRules.Validar(typeof(EventoComXmlAutorizado));
        Assert.False(resultado.IsSuccessful);

        var resultadoOk = EventPayloadRules.Validar(typeof(EventoComCertificadoId));
        Assert.True(resultadoOk.IsSuccessful);
    }

    [Fact]
    public void EventoIntegracao_CertificadoPfxByteArray_Falha()
    {
        // "certificado" já cai na denylist por nome — cobre o segundo exemplo do critério 6 (spec B3):
        // CertificadoPfx (byte[]) deve falhar tanto quanto XmlAutorizado.
        var resultado = EventPayloadRules.Validar(typeof(EventoComCertificadoPfx));

        Assert.False(resultado.IsSuccessful);
        Assert.Contains(resultado.FailingTypeNames, n => n.Contains("CertificadoPfx"));
    }

    [Fact]
    public void EventoIntegracao_PropriedadesHerdadasDaBase_NuncaFalhamAValidacao()
    {
        // MessageId (Guid), ContaId (Guid?) e OcorridoEm (DateTimeOffset) são herdadas de EventoIntegracao
        // por TODO evento — a regra não pode falsear positivo nelas, senão nenhum evento jamais passaria.
        var resultado = EventPayloadRules.Validar(typeof(EventoSomenteComBase));

        Assert.True(resultado.IsSuccessful, string.Join(", ", resultado.FailingTypeNames));
    }

    [Fact]
    public void EventoIntegracao_TipoQueNaoDerivaDeEventoIntegracao_Lanca()
    {
        Assert.Throws<ArgumentException>(() => EventPayloadRules.Validar(typeof(string)));
    }

    /// <summary>
    /// Spec B3, cabeçalho do passo 1 ("Contratos... sem dependência de EF") e plano de testes ("Messaging.Abstractions
    /// sem referência a EF/Npgsql"): a regra de payload em si vive num assembly que não pode carregar EF Core —
    /// senão a própria definição de "o que é um evento válido" vazaria detalhe de persistência para o Domain.
    /// </summary>
    [Fact]
    public void MessagingAbstractions_NaoReferenciaEntityFrameworkCoreNemNpgsql()
    {
        var assembly = typeof(EventoIntegracao).Assembly;

        var result = Types.InAssembly(assembly)
            .ShouldNot().HaveDependencyOnAny("Microsoft.EntityFrameworkCore", "Npgsql")
            .GetResult();

        Assert.True(result.IsSuccessful, string.Join(", ", result.FailingTypeNames ?? []));
    }

    /// <summary>Fixture: Guid terminado em "Id" — deve passar mesmo casando com a denylist por nome ("certificado").</summary>
    private sealed record EventoComCertificadoId : EventoIntegracao
    {
        public Guid CertificadoId { get; init; }
    }

    /// <summary>Fixture: string com nome que casa com a denylist ("xml") — deve falhar.</summary>
    private sealed record EventoComXmlAutorizado : EventoIntegracao
    {
        public string XmlAutorizado { get; init; } = string.Empty;
    }

    /// <summary>Fixture: byte[] com nome que casa com a denylist ("certificado", mas NÃO termina em "Id") — deve falhar.</summary>
    private sealed record EventoComCertificadoPfx : EventoIntegracao
    {
        public byte[] CertificadoPfx { get; init; } = [];
    }

    /// <summary>Fixture: nenhuma propriedade além das herdadas de EventoIntegracao.</summary>
    private sealed record EventoSomenteComBase : EventoIntegracao;
}

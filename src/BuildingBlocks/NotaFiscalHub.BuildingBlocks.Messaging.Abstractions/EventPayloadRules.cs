using System.Reflection;
using System.Text.RegularExpressions;

namespace NotaFiscalHub.BuildingBlocks.Messaging.Abstractions;

/// <summary>Resultado de <see cref="EventPayloadRules.Validar"/>, no formato usado pelas regras NetArchTest do projeto de testes de arquitetura.</summary>
public sealed class EventPayloadValidationResult
{
    public bool IsSuccessful { get; }
    public IReadOnlyList<string> FailingTypeNames { get; }

    internal EventPayloadValidationResult(bool isSuccessful, IReadOnlyList<string> failingTypeNames)
    {
        IsSuccessful = isSuccessful;
        FailingTypeNames = failingTypeNames;
    }
}

/// <summary>
/// Regra de arquitetura (spec B3 passo 5/critério 6): payload de <see cref="EventoIntegracao"/> só pode
/// carregar identificadores/status — nunca XML, PDF, PII ou blob bruto. Denylist de NOME de propriedade
/// (case-insensitive) com duas válvulas contra falso positivo: (a) isenção automática para propriedade
/// <see cref="Guid"/>/<see cref="Nullable{Guid}"/> cujo nome termina em "Id" (ex.: <c>CertificadoId</c> —
/// é exatamente o tipo de payload que o design manda usar); (b) allowlist explícita
/// <see cref="PropriedadesDeEventoPermitidas"/> para exceções pontuais, sempre com par (tipo, propriedade)
/// e comentário justificando no call-site do registro.
/// </summary>
public static class EventPayloadRules
{
    /// <summary>
    /// Denylist de nomes de propriedade (regex, case-insensitive): qualquer propriedade cujo nome contém
    /// um destes termos é candidata a carregar dado bruto/sensível — XML de documento fiscal, PDF, CPF,
    /// senha, material de certificado digital ou um payload genérico não tipado.
    /// </summary>
    private static readonly Regex Denylist = new("(?i)(xml|pdf|cpf|senha|certificado|payload)", RegexOptions.Compiled);

    /// <summary>
    /// Allowlist de exceções pontuais (tipo do evento, nome da propriedade). Cada entrada é uma decisão
    /// consciente — mesmo mecanismo de supressão do teste de fronteiras de módulo
    /// (<c>ArchitectureExceptions</c>, Tarefa 6): crescimento silencioso desta lista anula a garantia da
    /// regra, então toda entrada nova deve vir acompanhada de justificativa no PR.
    /// </summary>
    public static readonly IReadOnlyCollection<(Type TipoDoEvento, string Propriedade)> PropriedadesDeEventoPermitidas = [];

    public static EventPayloadValidationResult Validar(Type tipoDeEvento)
    {
        ArgumentNullException.ThrowIfNull(tipoDeEvento);

        if (!typeof(EventoIntegracao).IsAssignableFrom(tipoDeEvento))
        {
            throw new ArgumentException(
                $"{tipoDeEvento.FullName} não deriva de {nameof(EventoIntegracao)}.", nameof(tipoDeEvento));
        }

        var propriedadesOfensoras = tipoDeEvento
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => !EhPermitida(tipoDeEvento, p))
            .Select(p => $"{tipoDeEvento.Name}.{p.Name}")
            .ToList();

        return new EventPayloadValidationResult(propriedadesOfensoras.Count == 0, propriedadesOfensoras);
    }

    private static bool EhPermitida(Type tipoDeEvento, PropertyInfo propriedade)
    {
        if (!Denylist.IsMatch(propriedade.Name))
            return true;

        if (EhIdentificadorGuid(propriedade))
            return true;

        if (PropriedadesDeEventoPermitidas.Contains((tipoDeEvento, propriedade.Name)))
            return true;

        return false;
    }

    /// <summary>
    /// Válvula (a): <see cref="Guid"/> ou <see cref="Nullable{Guid}"/> cujo nome termina em "Id" — mesmo
    /// que o nome case com a denylist (ex.: <c>CertificadoId</c> contém "certificado"), é um identificador
    /// opaco, não o material sensível em si.
    /// </summary>
    private static bool EhIdentificadorGuid(PropertyInfo propriedade)
    {
        if (!propriedade.Name.EndsWith("Id", StringComparison.Ordinal))
            return false;

        var tipo = propriedade.PropertyType;
        return tipo == typeof(Guid) || tipo == typeof(Guid?);
    }
}

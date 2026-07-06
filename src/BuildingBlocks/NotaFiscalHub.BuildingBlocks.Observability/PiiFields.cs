namespace NotaFiscalHub.BuildingBlocks.Observability;

/// <summary>
/// Denylist central de nomes de propriedade estruturada considerados PII/dado sensível (spec B7 passo 2,
/// design §4.3/§5). Único lugar a tocar para adicionar um campo novo à política anti-PII — mantém a
/// Camada 1 (pipeline/<see cref="PiiMaskingEnricher"/>) e os testes T1 alinhados a uma única fonte de
/// verdade. Comparação é sempre case-insensitive (Serilog não normaliza nome de propriedade).
/// </summary>
public static class PiiFields
{
    /// <summary>
    /// Nomes mascarados totalmente (<c>"***"</c>). Prefixos (ex.: <c>xml</c>) são tratados à parte em
    /// <see cref="EhPrefixoSensivel"/> — nomes completos aqui casam por igualdade exata (case-insensitive).
    /// </summary>
    public static readonly IReadOnlySet<string> Denylist = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "cpf",
        "cnpjConsumidor",
        "documento",
        "nome",
        "nomeConsumidor",
        "destinatario",
        "email",
        "telefone",
        "endereco",
        "senha",
        "pfx",
        "csc",
    };

    /// <summary>Prefixos de nome que, quando casados (case-insensitive), disparam mascaramento total — ex.: <c>xmlAssinado</c>, <c>xmlNfe</c>.</summary>
    public static readonly IReadOnlyList<string> PrefixosSensiveis = ["xml"];

    /// <summary>
    /// Nomes mascarados PARCIALMENTE (formato <c>{6 primeiros}...{4 últimos}</c>) em vez de <c>"***"</c> —
    /// exceção deliberada da regra geral: a chave de acesso da NFe/NFCe precisa permanecer parcialmente
    /// identificável em log de suporte (design §4.3: "CPF/chave mascarados") sem expor o valor completo.
    /// </summary>
    public static readonly IReadOnlySet<string> MascaradosParcialmente = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "chaveAcesso",
    };

    /// <summary>
    /// <see langword="true"/> se <paramref name="nomePropriedade"/> deve ser mascarado — pertence à
    /// <see cref="Denylist"/>, casa um prefixo de <see cref="PrefixosSensiveis"/>, ou está em
    /// <see cref="MascaradosParcialmente"/> (mascaramento parcial também conta como "sensível" para
    /// quem só quer saber se deve tratar o valor com cuidado).
    /// </summary>
    public static bool EhSensivel(string nomePropriedade) =>
        Denylist.Contains(nomePropriedade) ||
        MascaradosParcialmente.Contains(nomePropriedade) ||
        EhPrefixoSensivel(nomePropriedade);

    private static bool EhPrefixoSensivel(string nomePropriedade) =>
        PrefixosSensiveis.Any(prefixo => nomePropriedade.StartsWith(prefixo, StringComparison.OrdinalIgnoreCase));
}

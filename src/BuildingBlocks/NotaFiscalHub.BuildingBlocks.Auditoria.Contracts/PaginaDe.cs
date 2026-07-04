namespace NotaFiscalHub.BuildingBlocks.Auditoria.Contracts;

/// <summary>
/// Página genérica de resultado paginado. Não existia em nenhum outro projeto da solution no momento da
/// Tarefa 5 (verificado por busca em <c>src/</c>) — introduzida aqui como o primeiro consumidor; candidata
/// a mover para um projeto mais genérico do kernel se um segundo consumidor precisar dela no futuro.
/// </summary>
/// <param name="Itens">Itens da página corrente.</param>
/// <param name="TotalDeItens">Total de itens em todas as páginas (para cálculo de paginação no cliente).</param>
/// <param name="Pagina">Número da página corrente (1-based).</param>
/// <param name="TamanhoPagina">Tamanho de página usado na consulta.</param>
public sealed record PaginaDe<T>(
    IReadOnlyList<T> Itens,
    int TotalDeItens,
    int Pagina,
    int TamanhoPagina);

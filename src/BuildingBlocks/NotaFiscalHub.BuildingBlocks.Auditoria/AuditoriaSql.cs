namespace NotaFiscalHub.BuildingBlocks.Auditoria;

/// <summary>
/// Construção da string SQL crua usada pelo <see cref="AuditoriaEventHandler"/> para a segunda barreira de
/// dedupe (spec B5 §Abordagem passo 4: "dedupe primário pelo inbox; segunda barreira no insert —
/// <c>ON CONFLICT (message_id) DO NOTHING</c> — duplicata e reentrega fora de ordem jamais geram segundo
/// registro nem falham o handler"). Extraída para permitir verificação de forma/conteúdo por teste de
/// unidade sem depender de PostgreSQL real (mesmo padrão de <c>OutboxSql</c>/<c>IdempotencySql</c>).
/// </summary>
internal static class AuditoriaSql
{
    public static string InserirRegistroSeAusente(string schema)
    {
        var tabela = $"\"{schema}\".\"registro_auditoria\"";

        return "INSERT INTO " + tabela + " " +
               "(id, conta_id, tipo_evento, acao, recurso_tipo, recurso_id, ator, ocorrido_em, " +
               "registrado_em, correlation_id, message_id, dados) " +
               "VALUES ({0}, {1}, {2}, {3}, {4}, {5}, {6}, {7}, {8}, {9}, {10}, {11}::jsonb) " +
               "ON CONFLICT (message_id) DO NOTHING";
    }
}

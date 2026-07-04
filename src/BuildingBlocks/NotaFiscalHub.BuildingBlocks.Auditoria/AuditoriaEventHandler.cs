using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using NotaFiscalHub.BuildingBlocks.Auditoria.Contracts;
using NotaFiscalHub.BuildingBlocks.Messaging.Abstractions;

namespace NotaFiscalHub.BuildingBlocks.Auditoria;

/// <summary>
/// Handler inbox (spec B5 §Abordagem passo 4) — materializa eventos de integração catalogados em
/// <c>auditoria.registro_auditoria</c>. Registrado UMA VEZ POR TIPO CONCRETO de evento auditável via
/// <c>AddInboxHandler&lt;AuditoriaDbContext, TEvento, AuditoriaEventHandler&gt;()</c> (ver
/// <c>ServiceCollectionExtensions.AddAuditoria</c>): o <c>OutboxTypeRegistry&lt;TDbContext&gt;</c> resolve
/// handlers por TIPO CLR CONCRETO da mensagem (<c>HandlersRegistrados(tipoClr)</c>), não por tipo base —
/// não existe mecanismo de "catch-all" no dispatcher (Tarefa 3), então o handler não pode ser inscrito uma
/// única vez contra <c>EventoIntegracao</c>. O <c>IAuditoriaEventCatalog</c> injetado é quem decide, EVENTO
/// A EVENTO, se o tipo é auditável — eventos concretos SEM handler registrado no dispatcher nunca chegam
/// aqui (ficam <c>SemHandler</c>, política de órfão da Tarefa 3, fora do escopo desta tarefa).
///
/// Dedupe em duas camadas: a Inbox (Tarefa 3, `(MessageId, Handler)`) é a barreira primária — já garantida
/// pelo <c>OutboxDispatcher</c> antes de invocar este handler; o <c>ON CONFLICT (message_id) DO NOTHING</c>
/// do insert abaixo é a SEGUNDA barreira, específica desta tabela (spec B5 critério de aceite 4).
///
/// <see cref="PiiSanitizer"/> é chamado incondicionalmente sobre o payload do evento ANTES do insert — não
/// existe caminho de persistência que pule a sanitização (ver <see cref="MontarDadosJson"/>).
/// </summary>
public sealed class AuditoriaEventHandler(AuditoriaDbContext db, IAuditoriaEventCatalog catalogo, ILogger<AuditoriaEventHandler> logger)
    : IInboxHandler<EventoIntegracao>
{
    public async Task HandleAsync(EventoIntegracao evento, MensagemContexto ctx, CancellationToken ct)
    {
        if (!catalogo.TryMap(evento, out var registro))
        {
            // Evento válido, mas fora do catálogo opt-in — consumido sem erro e sem registro
            // (spec B5 critério de aceite 7). Não é um caso de exceção nem de log de alerta.
            return;
        }

        var dadosJson = MontarDadosJson(evento, ctx);

        var schema = db.Model.FindEntityType(typeof(RegistroAuditoriaEntity))!.GetSchema()!;
        var sql = AuditoriaSql.InserirRegistroSeAusente(schema);

        await db.Database.ExecuteSqlRawAsync(
            sql,
            [
                Guid.CreateVersion7(),
                (object?)registro.ContaId ?? DBNull.Value,
                registro.TipoEvento,
                registro.Acao,
                registro.RecursoTipo,
                registro.RecursoId,
                registro.Ator,
                registro.OcorridoEm,
                DateTimeOffset.UtcNow,
                ctx.CorrelationId,
                evento.MessageId,
                (object?)dadosJson ?? DBNull.Value,
            ],
            ct);
    }

    /// <summary>
    /// Serializa o evento inteiro como payload bruto, sanitiza (remove chaves da denylist) e só então
    /// serializa o resultado sanitizado como o <c>dados</c> jsonb final — o <see cref="PiiSanitizer"/> roda
    /// SEMPRE, incondicionalmente, neste único ponto de montagem (spec B5 critério de aceite 5).
    /// </summary>
    private string? MontarDadosJson(EventoIntegracao evento, MensagemContexto ctx)
    {
        var payloadBruto = JsonSerializer.SerializeToElement(evento, evento.GetType());
        var payloadDict = new Dictionary<string, object?>();

        foreach (var propriedade in payloadBruto.EnumerateObject())
        {
            payloadDict[propriedade.Name] = ExtrairValor(propriedade.Value);
        }

        var payloadSanitizado = PiiSanitizer.Sanitizar(payloadDict, out var alertouWarn);

        if (alertouWarn)
        {
            // WARN sem ecoar valor OU nome de chave removida, por precaução extra (spec B5 §Abordagem
            // passo 2: "log de alerta WARN sem ecoar o valor").
            logger.LogWarning(
                "AuditoriaPiiSanitizada: chaves sensíveis removidas do payload antes de persistir. MessageId={MessageId}",
                ctx.MessageId);
        }

        return JsonSerializer.Serialize(payloadSanitizado);
    }

    private static object? ExtrairValor(JsonElement elemento) => elemento.ValueKind switch
    {
        JsonValueKind.String => elemento.GetString(),
        JsonValueKind.Number => elemento.GetDecimal(),
        JsonValueKind.True => true,
        JsonValueKind.False => false,
        JsonValueKind.Null => null,
        _ => elemento.GetRawText(),
    };
}

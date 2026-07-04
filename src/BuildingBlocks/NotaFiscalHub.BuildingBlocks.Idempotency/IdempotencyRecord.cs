namespace NotaFiscalHub.BuildingBlocks.Idempotency;

/// <summary>
/// Linha da tabela <c>kernel.idempotency_registro</c> (spec B4 §Abordagem passo 1/2). Chave primária
/// composta <c>(ContaId, Ambiente, Rota, Key)</c> — a rota E o ambiente compõem a chave: a mesma
/// <see cref="Key"/> reutilizada em rotas diferentes, ou na mesma rota com credenciais
/// <c>nfh_test_</c>/<c>nfh_live_</c> diferentes, gera registros independentes (spec B4 §Abordagem passos
/// 2/6, critérios de aceite 7 e 10).
/// </summary>
public sealed record IdempotencyRecord(
    Guid ContaId,
    string Ambiente, // "producao" | "homologacao" — derivado da credencial (nfh_live_/nfh_test_)
    string Key,
    string Rota, // template da rota, ex.: "POST /v1/nfce"
    string PayloadHashSha256, // hash dos BYTES crus do corpo
    IdempotencyState Estado,
    int? RespostaStatus,
    string? RespostaCorpo,
    string? RespostaContentType,
    string? RespostaLocation,
    DateTimeOffset CriadaEm,
    DateTimeOffset ExpiraEm);

public enum IdempotencyState
{
    EmProcessamento = 1,
    Concluida = 2,
}

using System.Security.Cryptography;

namespace NotaFiscalHub.BuildingBlocks.Idempotency;

/// <summary>
/// Hash SHA-256 do corpo CRU da requisição (spec B4 §Abordagem passo 3 e §Riscos "Hash por bytes crus"):
/// opera sobre os bytes exatamente como recebidos, sem qualquer canonicalização/reformatação de JSON — um
/// cliente que reserializa o corpo com espaçamento diferente produz um hash diferente e cai no caminho de
/// conflito (documentado ao integrador: "reenvie a requisição idêntica").
/// </summary>
public static class PayloadHasher
{
    public static string Sha256(byte[] corpo)
    {
        var hash = SHA256.HashData(corpo);
        return Convert.ToHexStringLower(hash);
    }
}

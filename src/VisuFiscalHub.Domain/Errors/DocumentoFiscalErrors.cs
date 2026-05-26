using VisuFiscalHub.Domain.Common;

namespace VisuFiscalHub.Domain.Errors;

public static class DocumentoFiscalErrors
{
    public static readonly Error NaoEncontrado =
        new("DocumentoFiscal.NaoEncontrado", "Documento fiscal não encontrado.");

    public static readonly Error IdempotencyKeyInvalida =
        new("DocumentoFiscal.IdempotencyKeyInvalida", "A chave de idempotência é inválida ou não foi informada.");

    public static readonly Error IdempotencyKeyJaUsada =
        new("DocumentoFiscal.IdempotencyKeyJaUsada", "A chave de idempotência já foi utilizada.");

    public static readonly Error StatusInvalidoParaOperacao =
        new("DocumentoFiscal.StatusInvalidoParaOperacao", "O status atual do documento não permite esta operação.");

    public static readonly Error TransicaoInvalida =
        new("DocumentoFiscal.TransicaoInvalida", "Transição de status inválida para o documento fiscal.");

    public static readonly Error TotalPagamentosInvalido =
        new("DocumentoFiscal.TotalPagamentosInvalido", "O total dos pagamentos não corresponde ao valor da nota.");

    public static readonly Error ValorTotalInvalido =
        new("DocumentoFiscal.ValorTotalInvalido", "O valor total da nota não corresponde ao somatório dos itens.");

    public static readonly Error PrazoDeCancelamentoExpirado =
        new("DocumentoFiscal.PrazoDeCancelamentoExpirado", "O prazo para cancelamento expirou.");

    public static readonly Error CpfInvalido =
        new("DocumentoFiscal.CpfInvalido", "O CPF do consumidor é inválido.");

    public static readonly Error NomeConsumidorObrigatorio =
        new("DocumentoFiscal.NomeConsumidorObrigatorio", "O nome do consumidor é obrigatório quando o CPF é informado.");

    public static readonly Error ChaveAcessoInvalida =
        new("DocumentoFiscal.ChaveAcessoInvalida", "A chave de acesso é inválida.");

    public static readonly Error QrCodeInvalido =
        new("DocumentoFiscal.QrCodeInvalido", "Os dados para geração do QR Code são inválidos.");

    public static readonly Error ProdutoInvalido =
        new("DocumentoFiscal.ProdutoInvalido", "Os dados do produto são inválidos.");

    public static readonly Error PagamentoInvalido =
        new("DocumentoFiscal.PagamentoInvalido", "Os dados do pagamento são inválidos.");

    public static readonly Error SemItens =
        new("DocumentoFiscal.SemItens", "O documento fiscal deve conter pelo menos um item.");

    public static readonly Error TenantInvalido =
        new("DocumentoFiscal.TenantInvalido", "O identificador de tenant é inválido.");

    public static readonly Error ClienteAppInvalido =
        new("DocumentoFiscal.ClienteAppInvalido", "O identificador de cliente app é inválido.");

    public static readonly Error TributoInvalido =
        new("DocumentoFiscal.TributoInvalido", "Os dados do tributo são inválidos.");

    // Valores válidos para NFC-e: 1=presencial, 3=telemarketing, 4=entrega domiciliar, 9=outros.
    // O valor 2 (internet) é explicitamente rejeitado pelo SEFAZ para NFC-e.
    public static readonly Error IndPresencaInvalido =
        new("DocumentoFiscal.IndPresencaInvalido", "Indicador de presença inválido. Valores permitidos para NFC-e: 1, 3, 4, 9.");

    public static readonly Error XmlIndisponivel =
        new("DocumentoFiscal.XmlIndisponivel", "O XML assinado ainda não está disponível. O documento pode estar em processamento.");

    public static readonly Error NfeDestinatarioInvalido =
        new("DocumentoFiscal.NfeDestinatarioInvalido", "Os dados do destinatário da NF-e são inválidos.");

    public static readonly Error DestinatarioObrigatorioParaNfe =
        new("DocumentoFiscal.DestinatarioObrigatorioParaNfe", "O destinatário é obrigatório para NF-e Modelo 55.");

    public static readonly Error DestinatarioNaoPermitidoEmNfce =
        new("DocumentoFiscal.DestinatarioNaoPermitidoEmNfce", "NFC-e Modelo 65 não suporta destinatário NF-e.");

    public static readonly Error NatOpObrigatoriaNfe =
        new("DocumentoFiscal.NatOpObrigatoriaNfe", "Natureza da operação é obrigatória para NF-e Modelo 55.");

    public static readonly Error TipoNaoSuportado =
        new("DocumentoFiscal.TipoNaoSuportado", "Tipo de documento não suportado nesta operação.");

    public static readonly Error TomadorObrigatorio =
        new("DocumentoFiscal.TomadorObrigatorio", "O tomador é obrigatório para NFS-e.");

    public static readonly Error ServicoNfseObrigatorio =
        new("DocumentoFiscal.ServicoNfseObrigatorio", "Os dados do serviço são obrigatórios para NFS-e.");

    public static readonly Error TomadorInvalido =
        new("DocumentoFiscal.TomadorInvalido", "Os dados do tomador da NFS-e são inválidos.");

    public static readonly Error ServicoNfseInvalido =
        new("DocumentoFiscal.ServicoNfseInvalido", "Os dados do serviço NFS-e são inválidos.");
}

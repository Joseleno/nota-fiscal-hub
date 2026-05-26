using Shouldly;
using VisuFiscalHub.Domain.Common;
using VisuFiscalHub.Domain.Entities;
using VisuFiscalHub.Domain.Enums;
using VisuFiscalHub.Domain.Identifiers;
using VisuFiscalHub.Domain.ValueObjects;

namespace VisuFiscalHub.Tests.Helpers;

/// <summary>
/// Constrói instâncias de <see cref="DocumentoFiscal"/> em diferentes estados para uso em testes.
/// Usa o domínio real — sem reflexão, sem mocks — garantindo que as transições de estado
/// obedecem às invariantes do domínio.
/// </summary>
internal static class DocumentoFiscalBuilder
{
    /// <summary>
    /// Cria um <see cref="DocumentoFiscal"/> em estado <c>Criado</c>.
    /// </summary>
    /// <param name="numero">
    /// Número sequencial da NF-e (padrão 1). Passe valores distintos quando o mesmo
    /// CNPJ é usado em múltiplos documentos no mesmo teste para evitar colisão de chave de acesso.
    /// </param>
    internal static DocumentoFiscal Criado(long numero = 1, DocumentoFiscalId? id = null)
    {
        var docId = id ?? DocumentoFiscalId.New();

        var chaveAcesso = ChaveAcesso.Gerar(
            cUF:    35,
            aamm:   "2601",
            cnpj:   "12345678000195",
            mod:    65,
            serie:  "001",
            nNF:    numero.ToString().PadLeft(9, '0'),
            tpEmis: TipoEmissao.Normal,
            cNF:    "12345678").Value;

        var tributo = Tributo.Criar(
            tipoIcms:        TipoIcms.CSOSN,
            csosnOuCst:      400,
            aliquotaIcms:    0m,
            baseCalculoIcms: 0m,
            valorIcms:       0m,
            cstPis:          CstPisCofins.Cst07,
            baseCalculoPis:  0m,
            aliquotaPis:     0m,
            valorPis:        0m,
            cstCofins:       CstPisCofins.Cst07,
            baseCalculoCofins: 0m,
            aliquotaCofins:  0m,
            valorCofins:     0m).Value;

        var produto = Produto.Criar(
            codigoProduto:   "PROD001",
            descricao:       "Produto Teste",
            ncm:             "12345678",
            cest:            null,
            cfopSaida:       "5102",
            unidadeComercial: "UN",
            quantidade:      1m,
            valorUnitario:   10m,
            valorDesconto:   0m,
            origemMercadoria: OrigemMercadoria.Nacional).Value;

        var item      = new ItemDocumento(1, produto, tributo);
        var pagamento = Pagamento.Criar(TipoPagamento.Dinheiro, 10m).Value;

        return DocumentoFiscal.Criar(
            id:             docId,
            tenantId:       new TenantId(Guid.NewGuid()),
            clienteAppId:   ClienteAppId.New(),
            idempotencyKey: $"idem-{docId.Value}",
            tipo:           TipoDocumento.NfCe,
            chaveAcesso:    chaveAcesso,
            numero:         numero,
            serie:          "001",
            indPresenca:    1,
            items:          [item],
            pagamentos:     [pagamento],
            timeProvider:   TimeProvider.System).Value;
    }

    /// <summary>Cria um <see cref="DocumentoFiscal"/> em estado <c>Enfileirado</c>.</summary>
    internal static DocumentoFiscal Enfileirado(long numero = 1, DocumentoFiscalId? id = null)
    {
        var doc = Criado(numero, id);
        doc.Enfileirar().IsSuccess.ShouldBeTrue("Enfileirar() falhou — verifique as invariantes do domínio.");
        return doc;
    }

    /// <summary>
    /// Cria um <see cref="DocumentoFiscal"/> em estado <c>Processando</c>.
    /// Representa o estado após IniciarProcessamento() ou retomada pós-crash.
    /// </summary>
    internal static DocumentoFiscal Processando(long numero = 1, DocumentoFiscalId? id = null)
    {
        var doc = Enfileirado(numero, id);
        doc.IniciarProcessamento().IsSuccess.ShouldBeTrue("IniciarProcessamento() falhou — verifique as invariantes do domínio.");
        return doc;
    }

    internal static DocumentoFiscal Autorizado(DateTimeOffset authorizedAt, long numero = 1)
    {
        var doc = Processando(numero);
        var qrCode = QrCode.Gerar(doc.ChaveAcesso!, AmbienteSefaz.Homologacao, "csc123", "https://exemplo.com").Value;
        doc.Autorizar("PROT001", "<xml/>", qrCode, authorizedAt, new FixedTimeProvider(authorizedAt))
           .IsSuccess.ShouldBeTrue("Autorizar() falhou no builder — verifique as invariantes.");
        doc.ClearDomainEvents();
        return doc;
    }

    internal static DocumentoFiscal Cancelando(DateTimeOffset authorizedAt, long numero = 1)
    {
        var doc = Autorizado(authorizedAt, numero);
        doc.IniciarCancelamento(new FixedTimeProvider(authorizedAt.AddMinutes(1)))
           .IsSuccess.ShouldBeTrue("IniciarCancelamento() falhou no builder — verifique as invariantes.");
        doc.ClearDomainEvents();
        return doc;
    }

    /// <summary>
    /// Cria um <see cref="DocumentoFiscal"/> com o status forçado via reflexão.
    /// Necessário exclusivamente para testar idempotência em status que não possuem
    /// transição pública direta (Autorizado, Cancelado, Falhou, Denegado).
    /// </summary>
    internal static DocumentoFiscal EmStatus(StatusDocumento status, long numero = 1)
    {
        var doc = Criado(numero);

        // Status sem transição prévia: retorna diretamente.
        if (status == StatusDocumento.Criado)
            return doc;

        if (status == StatusDocumento.Enfileirado)
        {
            doc.Enfileirar().IsSuccess.ShouldBeTrue();
            return doc;
        }

        // Qualquer status final requer Processando como pré-condição.
        doc.Enfileirar().IsSuccess.ShouldBeTrue();
        doc.IniciarProcessamento().IsSuccess.ShouldBeTrue();

        if (status == StatusDocumento.Processando)
            return doc;

        // Força o status via reflexão para testar cenários de idempotência onde não existe
        // transição pública (ex: Autorizado, Cancelado) sem passar pelo fluxo SEFAZ completo.
        var statusProp = typeof(DocumentoFiscal).GetProperty(
            nameof(DocumentoFiscal.Status),
            System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance)!;
        statusProp.SetValue(doc, status);

        return doc;
    }

    internal static NfeDestinatario ValidoNfeDestinatario() =>
        NfeDestinatario.Criar(
            cnpjOuCpf: "11222333000181",
            razaoSocial: "Empresa Teste Ltda",
            indIeDest: 9, ie: null,
            logradouro: "Rua Teste", numero: "100", complemento: null,
            bairro: "Centro", municipio: "São Paulo",
            codigoMunicipio: "3550308", uf: "SP", cep: "01310100",
            email: null).Value;

    internal static Result<DocumentoFiscal> CriarNfe(
        NfeDestinatario? destinatario,
        string? natOp = "Venda de mercadoria")
    {
        var chaveAcesso = ChaveAcesso.Gerar(
            cUF:    35,
            aamm:   "2601",
            cnpj:   "11222333000181",
            mod:    55,
            serie:  "001",
            nNF:    "000000001",
            tpEmis: TipoEmissao.Normal,
            cNF:    "12345678").Value;

        var tributo = Tributo.Criar(
            tipoIcms:        TipoIcms.CSOSN,
            csosnOuCst:      400,
            aliquotaIcms:    0m,
            baseCalculoIcms: 0m,
            valorIcms:       0m,
            cstPis:          CstPisCofins.Cst07,
            baseCalculoPis:  0m,
            aliquotaPis:     0m,
            valorPis:        0m,
            cstCofins:       CstPisCofins.Cst07,
            baseCalculoCofins: 0m,
            aliquotaCofins:  0m,
            valorCofins:     0m).Value;

        var produto = Produto.Criar(
            codigoProduto:   "PROD001",
            descricao:       "Produto Teste",
            ncm:             "12345678",
            cest:            null,
            cfopSaida:       "5102",
            unidadeComercial: "UN",
            quantidade:      1m,
            valorUnitario:   10m,
            valorDesconto:   0m,
            origemMercadoria: OrigemMercadoria.Nacional).Value;

        var item      = new ItemDocumento(1, produto, tributo);
        var pagamento = Pagamento.Criar(TipoPagamento.Dinheiro, 10m).Value;

        return DocumentoFiscal.Criar(
            id:             DocumentoFiscalId.New(),
            tenantId:       new TenantId(Guid.NewGuid()),
            clienteAppId:   ClienteAppId.New(),
            idempotencyKey: "idem-nfe-test",
            tipo:           TipoDocumento.NFe,
            chaveAcesso:    chaveAcesso,
            numero:         1,
            serie:          "001",
            indPresenca:    0,
            items:          [item],
            pagamentos:     [pagamento],
            timeProvider:   TimeProvider.System,
            nfeDestinatario: destinatario,
            natOp:          natOp);
    }

    internal static Result<DocumentoFiscal> CriarNfceComDestinatario(NfeDestinatario dest)
    {
        var chaveAcesso = ChaveAcesso.Gerar(
            cUF:    35,
            aamm:   "2601",
            cnpj:   "12345678000195",
            mod:    65,
            serie:  "001",
            nNF:    "000000002",
            tpEmis: TipoEmissao.Normal,
            cNF:    "12345678").Value;

        var tributo = Tributo.Criar(
            tipoIcms: TipoIcms.CSOSN, csosnOuCst: 400,
            aliquotaIcms: 0m, baseCalculoIcms: 0m, valorIcms: 0m,
            cstPis: CstPisCofins.Cst07, baseCalculoPis: 0m, aliquotaPis: 0m, valorPis: 0m,
            cstCofins: CstPisCofins.Cst07, baseCalculoCofins: 0m, aliquotaCofins: 0m, valorCofins: 0m).Value;

        var produto = Produto.Criar(
            codigoProduto: "PROD001", descricao: "Produto Teste", ncm: "12345678", cest: null,
            cfopSaida: "5102", unidadeComercial: "UN", quantidade: 1m, valorUnitario: 10m,
            valorDesconto: 0m, origemMercadoria: OrigemMercadoria.Nacional).Value;

        return DocumentoFiscal.Criar(
            id:             DocumentoFiscalId.New(),
            tenantId:       new TenantId(Guid.NewGuid()),
            clienteAppId:   ClienteAppId.New(),
            idempotencyKey: "idem-nfce-test",
            tipo:           TipoDocumento.NfCe,
            chaveAcesso:    chaveAcesso,
            numero:         2,
            serie:          "001",
            indPresenca:    1,
            items:          [new ItemDocumento(1, produto, tributo)],
            pagamentos:     [Pagamento.Criar(TipoPagamento.Dinheiro, 10m).Value],
            timeProvider:   TimeProvider.System,
            nfeDestinatario: dest);
    }

    internal static DocumentoFiscal AutorizadoNfe(DateTimeOffset authorizedAt)
    {
        var dest = ValidoNfeDestinatario();
        var doc  = CriarNfe(dest).Value;
        doc.Enfileirar().IsSuccess.ShouldBeTrue();
        doc.IniciarProcessamento().IsSuccess.ShouldBeTrue();
        var qrCode = QrCode.Gerar(doc.ChaveAcesso!, AmbienteSefaz.Homologacao, "csc123", "https://exemplo.com").Value;
        doc.Autorizar("PROT001", "<xml/>", qrCode, authorizedAt, new FixedTimeProvider(authorizedAt))
           .IsSuccess.ShouldBeTrue("Autorizar() falhou no builder NF-e.");
        doc.ClearDomainEvents();
        return doc;
    }
}

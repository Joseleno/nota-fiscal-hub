using Shouldly;
using VisuFiscalHub.Domain.Common;
using VisuFiscalHub.Domain.Entities;
using VisuFiscalHub.Domain.Enums;
using VisuFiscalHub.Domain.Errors;
using VisuFiscalHub.Domain.Events;
using VisuFiscalHub.Domain.Identifiers;
using VisuFiscalHub.Domain.ValueObjects;
using VisuFiscalHub.Tests.Helpers;

namespace VisuFiscalHub.Tests.Domain;

public class DocumentoFiscalTests
{
    private static readonly DateTimeOffset FixedNow = new(2026, 1, 15, 12, 0, 0, TimeSpan.Zero);
    private static TimeProvider FixedTime(DateTimeOffset at) => new FixedTimeProvider(at);

    [Fact]
    public void Enfileirar_QuandoCriado_DeveTransicionarParaEnfileirado()
    {
        var doc = DocumentoFiscalBuilder.Criado();
        var result = doc.Enfileirar();
        result.IsSuccess.ShouldBeTrue();
        doc.Status.ShouldBe(StatusDocumento.Enfileirado);
    }

    [Fact]
    public void IniciarProcessamento_QuandoEnfileirado_DeveTransicionarParaProcessando()
    {
        var doc = DocumentoFiscalBuilder.Enfileirado();
        var result = doc.IniciarProcessamento();
        result.IsSuccess.ShouldBeTrue();
        doc.Status.ShouldBe(StatusDocumento.Processando);
    }

    [Fact]
    public void Autorizar_QuandoProcessando_DeveTransicionarParaAutorizado()
    {
        var doc = DocumentoFiscalBuilder.Processando();
        var qrCode = QrCode.Gerar(doc.ChaveAcesso!, AmbienteSefaz.Homologacao, "csc123", "https://exemplo.com").Value;
        var result = doc.Autorizar("PROT001", "<xml/>", qrCode, FixedNow, FixedTime(FixedNow));
        result.IsSuccess.ShouldBeTrue();
        doc.Status.ShouldBe(StatusDocumento.Autorizado);
        doc.Protocolo.ShouldBe("PROT001");
    }

    [Fact]
    public void Rejeitar_QuandoProcessando_DeveTransicionarParaRejeitado()
    {
        var doc = DocumentoFiscalBuilder.Processando();
        var result = doc.Rejeitar("Rejeição 999", FixedTime(FixedNow));
        result.IsSuccess.ShouldBeTrue();
        doc.Status.ShouldBe(StatusDocumento.Rejeitado);
        doc.MotivoRejeicao.ShouldBe("Rejeição 999");
    }

    [Fact]
    public void Falhar_QuandoProcessando_DeveTransicionarParaFalhou()
    {
        var doc = DocumentoFiscalBuilder.Processando();
        var result = doc.Falhar(FixedTime(FixedNow));
        result.IsSuccess.ShouldBeTrue();
        doc.Status.ShouldBe(StatusDocumento.Falhou);
    }

    [Fact]
    public void Denegar_QuandoProcessando_DeveTransicionarParaDenegado()
    {
        var doc = DocumentoFiscalBuilder.Processando();
        var result = doc.Denegar("CNPJ irregular", "14200167140065", FixedTime(FixedNow));
        result.IsSuccess.ShouldBeTrue();
        doc.Status.ShouldBe(StatusDocumento.Denegado);
    }

    [Fact]
    public void Autorizar_QuandoJaAutorizado_DeveRetornarErro_NaoLancarExcecao()
    {
        var doc = DocumentoFiscalBuilder.EmStatus(StatusDocumento.Autorizado);
        var qrCode = QrCode.Gerar(doc.ChaveAcesso!, AmbienteSefaz.Homologacao, "csc123", "https://exemplo.com").Value;
        Result result = default!;
        Should.NotThrow(() => result = doc.Autorizar("PROT002", "<xml/>", qrCode, FixedNow, FixedTime(FixedNow)));
        result.IsFailure.ShouldBeTrue();
    }

    [Fact]
    public void Rejeitar_QuandoAutorizado_DeveRetornarErro()
    {
        var doc = DocumentoFiscalBuilder.EmStatus(StatusDocumento.Autorizado);
        var result = doc.Rejeitar("motivo", FixedTime(FixedNow));
        result.IsFailure.ShouldBeTrue();
    }

    [Fact]
    public void Enfileirar_QuandoJaEnfileirado_DeveRetornarErro()
    {
        var doc = DocumentoFiscalBuilder.Enfileirado();
        var result = doc.Enfileirar();
        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe(DocumentoFiscalErrors.TransicaoInvalida.Code);
    }

    [Fact]
    public void Autorizar_DevePublicarDocumentoFiscalAutorizadoEvent()
    {
        var doc = DocumentoFiscalBuilder.Processando();
        var qrCode = QrCode.Gerar(doc.ChaveAcesso!, AmbienteSefaz.Homologacao, "csc123", "https://exemplo.com").Value;
        doc.Autorizar("PROT001", "<xml/>", qrCode, FixedNow, FixedTime(FixedNow));
        doc.DomainEvents.OfType<DocumentoFiscalAutorizadoEvent>().ShouldHaveSingleItem();
    }

    [Fact]
    public void Rejeitar_DeveGravarMotivoNaPropriedade()
    {
        var doc = DocumentoFiscalBuilder.Processando();
        doc.Rejeitar("cStat 999 motivo", FixedTime(FixedNow));
        doc.MotivoRejeicao.ShouldBe("cStat 999 motivo");
    }

    [Fact]
    public void Denegar_DevePublicarDocumentoFiscalDenegadoEvent()
    {
        var doc = DocumentoFiscalBuilder.Processando();
        doc.Denegar("CNPJ irregular", "14200167140065", FixedTime(FixedNow));
        doc.DomainEvents.OfType<DocumentoFiscalDenegadoEvent>().ShouldHaveSingleItem();
    }

    [Fact]
    public void Falhar_DevePublicarDocumentoFiscalFalhouEvent()
    {
        var doc = DocumentoFiscalBuilder.Processando();
        doc.Falhar(FixedTime(FixedNow));
        doc.DomainEvents.OfType<DocumentoFiscalFalhouEvent>().ShouldHaveSingleItem();
    }

    // ──────────────────────────────────────────────────────────────
    // IniciarCancelamento
    // ──────────────────────────────────────────────────────────────

    [Fact]
    public void IniciarCancelamento_QuandoAutorizadoDentroDoPrazo_TransicionaParaCancelando()
    {
        var documento = DocumentoFiscalBuilder.Autorizado(FixedNow.AddMinutes(-10));
        var result = documento.IniciarCancelamento(new FixedTimeProvider(FixedNow));
        result.IsSuccess.ShouldBeTrue();
        documento.Status.ShouldBe(StatusDocumento.Cancelando);
        documento.MotivoRejeicao.ShouldBeNull();
        documento.DomainEvents.ShouldBeEmpty();
    }

    [Fact]
    public void IniciarCancelamento_QuandoPrazoExpirado_RetornaErro()
    {
        var authorizedAt = FixedNow.AddMinutes(-31);
        var documento = DocumentoFiscalBuilder.Autorizado(authorizedAt);
        var result = documento.IniciarCancelamento(new FixedTimeProvider(FixedNow));
        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("DocumentoFiscal.PrazoDeCancelamentoExpirado");
        documento.Status.ShouldBe(StatusDocumento.Autorizado);
    }

    [Fact]
    public void IniciarCancelamento_QuandoExatamente30Minutos_RetornaErro()
    {
        var authorizedAt = FixedNow.AddHours(-1);
        var documento = DocumentoFiscalBuilder.Autorizado(authorizedAt);
        var result = documento.IniciarCancelamento(new FixedTimeProvider(authorizedAt.AddMinutes(30)));
        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("DocumentoFiscal.PrazoDeCancelamentoExpirado");
    }

    [Fact]
    public void IniciarCancelamento_QuandoNaoAutorizado_RetornaErro()
    {
        var documento = DocumentoFiscalBuilder.Enfileirado();
        var result = documento.IniciarCancelamento(new FixedTimeProvider(FixedNow));
        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("DocumentoFiscal.TransicaoInvalida");
    }

    [Fact]
    public void IniciarCancelamento_QuandoJaCancelando_RetornaErro()
    {
        var documento = DocumentoFiscalBuilder.Cancelando(FixedNow.AddMinutes(-5));
        var result = documento.IniciarCancelamento(new FixedTimeProvider(FixedNow));
        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("DocumentoFiscal.TransicaoInvalida");
    }

    [Fact]
    public void IniciarCancelamento_QuandoCancelado_RetornaErro()
    {
        var documento = DocumentoFiscalBuilder.Cancelando(FixedNow.AddMinutes(-5));
        documento.ConfirmarCancelamento(FixedNow, new FixedTimeProvider(FixedNow));
        var result = documento.IniciarCancelamento(new FixedTimeProvider(FixedNow));
        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("DocumentoFiscal.TransicaoInvalida");
    }

    // ──────────────────────────────────────────────────────────────
    // ConfirmarCancelamento
    // ──────────────────────────────────────────────────────────────

    [Fact]
    public void ConfirmarCancelamento_QuandoCancelando_TransicionaParaCancelado()
    {
        var documento = DocumentoFiscalBuilder.Cancelando(FixedNow.AddMinutes(-5));
        var result = documento.ConfirmarCancelamento(FixedNow, new FixedTimeProvider(FixedNow));
        result.IsSuccess.ShouldBeTrue();
        documento.Status.ShouldBe(StatusDocumento.Cancelado);
        documento.CanceladoAt.ShouldBe(FixedNow);
        documento.DomainEvents.ShouldHaveSingleItem().ShouldBeOfType<DocumentoFiscalCanceladoEvent>();
    }

    [Fact]
    public void ConfirmarCancelamento_QuandoNaoCancelando_RetornaErro()
    {
        var documento = DocumentoFiscalBuilder.Autorizado(FixedNow.AddMinutes(-5));
        var result = documento.ConfirmarCancelamento(FixedNow, new FixedTimeProvider(FixedNow));
        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("DocumentoFiscal.TransicaoInvalida");
    }

    // ──────────────────────────────────────────────────────────────
    // RejeitarCancelamento
    // ──────────────────────────────────────────────────────────────

    [Fact]
    public void RejeitarCancelamento_QuandoCancelando_RevertaParaAutorizado()
    {
        var documento = DocumentoFiscalBuilder.Cancelando(FixedNow.AddMinutes(-5));
        var result = documento.RejeitarCancelamento("Prazo encerrado no SEFAZ");
        result.IsSuccess.ShouldBeTrue();
        documento.Status.ShouldBe(StatusDocumento.Autorizado);
        documento.MotivoRejeicao.ShouldBe("Prazo encerrado no SEFAZ");
        documento.DomainEvents.ShouldBeEmpty();
    }

    [Fact]
    public void RejeitarCancelamento_QuandoNaoCancelando_RetornaErro()
    {
        var documento = DocumentoFiscalBuilder.Autorizado(FixedNow.AddMinutes(-5));
        var result = documento.RejeitarCancelamento("motivo");
        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("DocumentoFiscal.TransicaoInvalida");
    }

    [Fact]
    public void IniciarCancelamento_AposaRejeicao_LimpaMotivo()
    {
        var authorizedAt = FixedNow.AddMinutes(-5);
        var documento = DocumentoFiscalBuilder.Cancelando(authorizedAt);
        documento.RejeitarCancelamento("Motivo anterior");
        var result = documento.IniciarCancelamento(new FixedTimeProvider(FixedNow));
        result.IsSuccess.ShouldBeTrue();
        documento.MotivoRejeicao.ShouldBeNull();
    }

    [Fact]
    public void IniciarCancelamento_AposRejeicaoComPrazoExpirado_RetornaErro()
    {
        // Documento autorizado 35 minutos atrás; SEFAZ rejeitou o cancelamento (ex.: cStat=218).
        // ClienteApp tenta novamente — mas o prazo de 30 min já expirou.
        var authorizedAt = FixedNow.AddMinutes(-35);
        var documento = DocumentoFiscalBuilder.Cancelando(authorizedAt);
        documento.RejeitarCancelamento("Rejeição: Prazo de Cancelamento Superior ao Prazo Limite");
        var result = documento.IniciarCancelamento(new FixedTimeProvider(FixedNow));
        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("DocumentoFiscal.PrazoDeCancelamentoExpirado");
        documento.Status.ShouldBe(StatusDocumento.Autorizado);
    }

    [Fact]
    public void RejeitarCancelamento_ComMotivoVazio_RetornaErro()
    {
        var documento = DocumentoFiscalBuilder.Cancelando(FixedNow.AddMinutes(-5));
        var result = documento.RejeitarCancelamento(string.Empty);
        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("DocumentoFiscal.TransicaoInvalida");
        documento.Status.ShouldBe(StatusDocumento.Cancelando);
    }

    // ── NFSe guards no Criar ────────────────────────────────────────────────

    [Fact]
    public void Criar_NFSe_SemTomador_RetornaFalha()
    {
        var result = DocumentoFiscal.Criar(
            id: new DocumentoFiscalId(Guid.NewGuid()),
            tenantId: new TenantId(Guid.NewGuid()),
            clienteAppId: new ClienteAppId(Guid.NewGuid()),
            idempotencyKey: Guid.NewGuid().ToString(),
            tipo: TipoDocumento.NFSe,
            chaveAcesso: null,
            numero: 1,
            serie: "001",
            indPresenca: 1,
            items: new[] { ItemDocumentoValido() },
            pagamentos: new[] { PagamentoValido(10m) },
            timeProvider: TimeProvider.System,
            tomador: null,
            servicoNfse: ServicoNfseValido());

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("DocumentoFiscal.TomadorObrigatorio");
    }

    [Fact]
    public void Criar_NFSe_SemServicoNfse_RetornaFalha()
    {
        var result = DocumentoFiscal.Criar(
            id: new DocumentoFiscalId(Guid.NewGuid()),
            tenantId: new TenantId(Guid.NewGuid()),
            clienteAppId: new ClienteAppId(Guid.NewGuid()),
            idempotencyKey: Guid.NewGuid().ToString(),
            tipo: TipoDocumento.NFSe,
            chaveAcesso: null,
            numero: 1,
            serie: "001",
            indPresenca: 1,
            items: new[] { ItemDocumentoValido() },
            pagamentos: new[] { PagamentoValido(10m) },
            timeProvider: TimeProvider.System,
            tomador: TomadorValido(),
            servicoNfse: null);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("DocumentoFiscal.ServicoNfseObrigatorio");
    }

    [Fact]
    public void Criar_NFSe_ComTomadorEServico_Sucesso()
    {
        var result = DocumentoFiscal.Criar(
            id: new DocumentoFiscalId(Guid.NewGuid()),
            tenantId: new TenantId(Guid.NewGuid()),
            clienteAppId: new ClienteAppId(Guid.NewGuid()),
            idempotencyKey: Guid.NewGuid().ToString(),
            tipo: TipoDocumento.NFSe,
            chaveAcesso: null,
            numero: 1,
            serie: "001",
            indPresenca: 1,
            items: new[] { ItemDocumentoValido() },
            pagamentos: new[] { PagamentoValido(10m) },
            timeProvider: TimeProvider.System,
            tomador: TomadorValido(),
            servicoNfse: ServicoNfseValido());

        result.IsSuccess.ShouldBeTrue();
        result.Value.Tomador.ShouldNotBeNull();
        result.Value.ServicoNfse.ShouldNotBeNull();
        result.Value.ChaveAcesso.ShouldBeNull();
    }

    [Fact]
    public void Criar_NfCe_ComTomador_RetornaFalha()
    {
        var result = DocumentoFiscal.Criar(
            id: new DocumentoFiscalId(Guid.NewGuid()),
            tenantId: new TenantId(Guid.NewGuid()),
            clienteAppId: new ClienteAppId(Guid.NewGuid()),
            idempotencyKey: Guid.NewGuid().ToString(),
            tipo: TipoDocumento.NfCe,
            chaveAcesso: ChaveAcessoValida(),
            numero: 1,
            serie: "001",
            indPresenca: 1,
            items: new[] { ItemDocumentoValido() },
            pagamentos: new[] { PagamentoValido(10m) },
            timeProvider: TimeProvider.System,
            tomador: TomadorValido(),
            servicoNfse: null);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("DocumentoFiscal.TipoNaoSuportado");
    }

    // ── Helpers privados para testes NFSe ────────────────────────────────────

    private static ChaveAcesso ChaveAcessoValida() => ChaveAcesso.Gerar(
        cUF: 35,
        aamm: "2601",
        cnpj: "12345678000195",
        mod: 65,
        serie: "001",
        nNF: "000000001",
        tpEmis: TipoEmissao.Normal,
        cNF: "12345678").Value;

    private static ItemDocumento ItemDocumentoValido()
    {
        var tributo = Tributo.Criar(
            tipoIcms: TipoIcms.CSOSN, csosnOuCst: 400,
            aliquotaIcms: 0m, baseCalculoIcms: 0m, valorIcms: 0m,
            cstPis: CstPisCofins.Cst07, baseCalculoPis: 0m, aliquotaPis: 0m, valorPis: 0m,
            cstCofins: CstPisCofins.Cst07, baseCalculoCofins: 0m, aliquotaCofins: 0m, valorCofins: 0m).Value;

        var produto = Produto.Criar(
            codigoProduto: "PROD001",
            descricao: "Produto Teste",
            ncm: "12345678",
            cest: null,
            cfopSaida: "5102",
            unidadeComercial: "UN",
            quantidade: 1m,
            valorUnitario: 10m,
            valorDesconto: 0m,
            origemMercadoria: OrigemMercadoria.Nacional).Value;

        return new ItemDocumento(1, produto, tributo);
    }

    private static Pagamento PagamentoValido(decimal valor) =>
        Pagamento.Criar(TipoPagamento.Dinheiro, valor).Value;

    private static Tomador TomadorValido() => Tomador.Criar(
        cnpjOuCpf: "11222333000181",
        razaoSocial: "Empresa Tomadora Ltda",
        logradouro: "Rua Teste", numero: "100", complemento: null,
        bairro: "Centro", municipio: "São Paulo", codigoMunicipio: "3550308",
        uf: "SP", cep: "01310100", email: null, inscricaoMunicipal: null).Value;

    private static ServicoNfse ServicoNfseValido() => ServicoNfse.Criar(
        codigoServico: "1.01",
        discriminacao: "Desenvolvimento de software",
        codigoTributacaoMunicipio: null,
        aliquotaIss: 2m,
        baseCalculoIss: 1000m,
        valorIss: 20m,
        valorDeducoes: null,
        issRetido: false).Value;

    // ──────────────────────────────────────────────────────────────
    // NF-e specific rules
    // ──────────────────────────────────────────────────────────────

    [Fact]
    public void Criar_Nfe_SemDestinatario_RetornaErro()
    {
        var result = DocumentoFiscalBuilder.CriarNfe(destinatario: null);
        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("DocumentoFiscal.DestinatarioObrigatorioParaNfe");
    }

    [Fact]
    public void Criar_Nfe_ComDestinatario_Sucesso()
    {
        var result = DocumentoFiscalBuilder.CriarNfe(destinatario: DocumentoFiscalBuilder.ValidoNfeDestinatario());
        result.IsSuccess.ShouldBeTrue();
        result.Value.NfeDestinatario.ShouldNotBeNull();
    }

    [Fact]
    public void Criar_Nfe_SemNatOp_RetornaErro()
    {
        var result = DocumentoFiscalBuilder.CriarNfe(
            destinatario: DocumentoFiscalBuilder.ValidoNfeDestinatario(),
            natOp: null);
        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("DocumentoFiscal.NatOpObrigatoriaNfe");
    }

    [Fact]
    public void Criar_Nfce_ComDestinatario_RetornaErro()
    {
        var result = DocumentoFiscalBuilder.CriarNfceComDestinatario(
            DocumentoFiscalBuilder.ValidoNfeDestinatario());
        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("DocumentoFiscal.DestinatarioNaoPermitidoEmNfce");
    }

    [Fact]
    public void IniciarCancelamento_Nfe_DentroDe24h_Sucesso()
    {
        var authorizedAt = FixedNow.AddHours(-23);
        var documento = DocumentoFiscalBuilder.AutorizadoNfe(authorizedAt);
        var result = documento.IniciarCancelamento(new FixedTimeProvider(FixedNow));
        result.IsSuccess.ShouldBeTrue();
        documento.Status.ShouldBe(StatusDocumento.Cancelando);
    }

    [Fact]
    public void IniciarCancelamento_Nfe_Apos24h_RetornaErro()
    {
        var authorizedAt = FixedNow.AddHours(-25);
        var documento = DocumentoFiscalBuilder.AutorizadoNfe(authorizedAt);
        var result = documento.IniciarCancelamento(new FixedTimeProvider(FixedNow));
        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("DocumentoFiscal.PrazoDeCancelamentoExpirado");
    }

    [Fact]
    public void IniciarCancelamento_Nfce_Apos30Min_RetornaErro()
    {
        var authorizedAt = FixedNow.AddMinutes(-31);
        var documento = DocumentoFiscalBuilder.Autorizado(authorizedAt);
        var result = documento.IniciarCancelamento(new FixedTimeProvider(FixedNow));
        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("DocumentoFiscal.PrazoDeCancelamentoExpirado");
    }

    [Fact]
    public void IniciarCancelamento_NFe_Usa24h_NfCe_Usa30min()
    {
        // NF-e: dentro do prazo de 24h deve cancelar
        var docNfe = DocumentoFiscalBuilder.AutorizadoNfe(FixedNow.AddHours(-23));
        var resultNfeDentro = docNfe.IniciarCancelamento(new FixedTimeProvider(FixedNow));
        resultNfeDentro.IsSuccess.ShouldBeTrue();

        // NF-e: fora do prazo de 24h deve rejeitar
        var docNfe2 = DocumentoFiscalBuilder.AutorizadoNfe(FixedNow.AddHours(-25));
        var resultNfeFora = docNfe2.IniciarCancelamento(new FixedTimeProvider(FixedNow));
        resultNfeFora.IsFailure.ShouldBeTrue();
        resultNfeFora.Error.Code.ShouldBe("DocumentoFiscal.PrazoDeCancelamentoExpirado");

        // NFC-e: dentro do prazo de 30min deve cancelar
        var docNfCe = DocumentoFiscalBuilder.Autorizado(FixedNow.AddMinutes(-29));
        var resultNfCeDentro = docNfCe.IniciarCancelamento(new FixedTimeProvider(FixedNow));
        resultNfCeDentro.IsSuccess.ShouldBeTrue();

        // NFC-e: fora do prazo de 30min deve rejeitar
        var docNfCe2 = DocumentoFiscalBuilder.Autorizado(FixedNow.AddMinutes(-31));
        var resultNfCeFora = docNfCe2.IniciarCancelamento(new FixedTimeProvider(FixedNow));
        resultNfCeFora.IsFailure.ShouldBeTrue();
        resultNfCeFora.Error.Code.ShouldBe("DocumentoFiscal.PrazoDeCancelamentoExpirado");
    }

    [Fact]
    public void Criar_NFSe_SemItens_Sucesso()
    {
        var tenant = NfseTestHelpers.CriarTenantNfse();
        var tomador = NfseTestHelpers.TomadorValido();
        var servico = NfseTestHelpers.ServicoNfseValido();

        var result = DocumentoFiscal.Criar(
            id: new DocumentoFiscalId(Guid.NewGuid()),
            tenantId: tenant.Id,
            clienteAppId: tenant.ClienteAppId,
            idempotencyKey: Guid.NewGuid().ToString(),
            tipo: TipoDocumento.NFSe,
            chaveAcesso: null,
            numero: 1L,
            serie: "001",
            indPresenca: 0,
            items: [],
            pagamentos: [],
            timeProvider: TimeProvider.System,
            tomador: tomador,
            servicoNfse: servico);

        result.IsSuccess.ShouldBeTrue("NFSe deve ser criada sem itens de produto.");
    }
}

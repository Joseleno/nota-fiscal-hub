using Shouldly;
using VisuFiscalHub.Domain.Common;
using VisuFiscalHub.Domain.Enums;
using VisuFiscalHub.Domain.Errors;
using VisuFiscalHub.Domain.Events;
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
        var qrCode = QrCode.Gerar(doc.ChaveAcesso, AmbienteSefaz.Homologacao, "csc123", "https://exemplo.com").Value;
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
        var qrCode = QrCode.Gerar(doc.ChaveAcesso, AmbienteSefaz.Homologacao, "csc123", "https://exemplo.com").Value;
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
        var qrCode = QrCode.Gerar(doc.ChaveAcesso, AmbienteSefaz.Homologacao, "csc123", "https://exemplo.com").Value;
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
}

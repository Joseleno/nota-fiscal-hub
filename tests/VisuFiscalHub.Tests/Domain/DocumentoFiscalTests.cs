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
    public void Cancelar_QuandoCriado_DeveRetornarErro()
    {
        var doc = DocumentoFiscalBuilder.Criado();
        var result = doc.Cancelar(FixedTime(FixedNow));
        result.IsFailure.ShouldBeTrue();
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

    [Fact]
    public void Cancelar_QuandoDentro30Minutos_DeveTransicionarParaCancelado()
    {
        var authorizedAt = FixedNow;
        var doc = DocumentoFiscalBuilder.Processando();
        var qrCode = QrCode.Gerar(doc.ChaveAcesso, AmbienteSefaz.Homologacao, "csc123", "https://exemplo.com").Value;
        doc.Autorizar("PROT001", "<xml/>", qrCode, authorizedAt, FixedTime(authorizedAt));

        var result = doc.Cancelar(FixedTime(authorizedAt.AddMinutes(29)));

        result.IsSuccess.ShouldBeTrue();
        doc.Status.ShouldBe(StatusDocumento.Cancelado);
    }

    [Fact]
    public void Cancelar_QuandoFora30Minutos_DeveRetornarErro()
    {
        var authorizedAt = FixedNow;
        var doc = DocumentoFiscalBuilder.Processando();
        var qrCode = QrCode.Gerar(doc.ChaveAcesso, AmbienteSefaz.Homologacao, "csc123", "https://exemplo.com").Value;
        doc.Autorizar("PROT001", "<xml/>", qrCode, authorizedAt, FixedTime(authorizedAt));

        var result = doc.Cancelar(FixedTime(authorizedAt.AddMinutes(31)));

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe(DocumentoFiscalErrors.PrazoDeCancelamentoExpirado.Code);
    }

    [Fact]
    public void Cancelar_QuandoExatamente30Minutos_DeveRetornarErro()
    {
        var authorizedAt = FixedNow;
        var doc = DocumentoFiscalBuilder.Processando();
        var qrCode = QrCode.Gerar(doc.ChaveAcesso, AmbienteSefaz.Homologacao, "csc123", "https://exemplo.com").Value;
        doc.Autorizar("PROT001", "<xml/>", qrCode, authorizedAt, FixedTime(authorizedAt));

        var result = doc.Cancelar(FixedTime(authorizedAt.AddMinutes(30)));

        result.IsFailure.ShouldBeTrue("documento autorizado há exatamente 30min não pode ser cancelado");
        result.Error.Code.ShouldBe(DocumentoFiscalErrors.PrazoDeCancelamentoExpirado.Code);
    }
}

using VisuFiscalHub.Domain.Common;
using VisuFiscalHub.Domain.Enums;
using VisuFiscalHub.Domain.Errors;
using VisuFiscalHub.Domain.Events;
using VisuFiscalHub.Domain.Identifiers;
using VisuFiscalHub.Domain.ValueObjects;

namespace VisuFiscalHub.Domain.Entities;

public sealed class DocumentoFiscal : Entity<DocumentoFiscalId>
{
    // Valores válidos por tipo de documento per SEFAZ NT 2019.001.
    // NFC-e: valor 2 (internet) é explicitamente rejeitado; NF-e aceita 0-5 e 9.
    private static readonly IReadOnlySet<int> IndPresencaNfce = new HashSet<int> { 1, 3, 4, 9 };
    private static readonly IReadOnlySet<int> IndPresencaNfe  = new HashSet<int> { 0, 1, 2, 3, 4, 5, 9 };

    private readonly List<ItemDocumento> _items = [];
    private readonly List<Pagamento> _pagamentos = [];

    private DocumentoFiscal()
    {
        // Para EF Core — inicialização via reflexão.
        // null! é justificado: EF Core popula estas propriedades via reflexão após instanciar.
        IdempotencyKey = string.Empty;
        Serie = string.Empty;
        ChaveAcesso = null!;
    }

    private DocumentoFiscal(
        DocumentoFiscalId id,
        TenantId tenantId,
        ClienteAppId clienteAppId,
        string idempotencyKey,
        TipoDocumento tipo,
        ChaveAcesso chaveAcesso,
        long numero,
        string serie,
        int indPresenca,
        string? cpfConsumidor,
        string? nomeConsumidor,
        List<ItemDocumento> items,
        List<Pagamento> pagamentos,
        DateTimeOffset createdAt,
        NfeDestinatario? nfeDestinatario,
        string? natOp,
        int modFrete) : base(id)
    {
        TenantId = tenantId;
        ClienteAppId = clienteAppId;
        IdempotencyKey = idempotencyKey;
        Tipo = tipo;
        ChaveAcesso = chaveAcesso;
        Numero = numero;
        Serie = serie;
        IndPresenca = indPresenca;
        CpfConsumidor = cpfConsumidor;
        NomeConsumidor = nomeConsumidor;
        Status = StatusDocumento.Criado;
        _items = items;
        _pagamentos = pagamentos;
        CreatedAt = createdAt;
        NfeDestinatario = nfeDestinatario;
        NatOp = natOp;
        ModFrete = modFrete;
    }

    public TenantId TenantId { get; private set; }

    // Armazenado para evitar query adicional ao publicar DocumentoFiscalAutorizadoEvent/DenegadoEvent
    public ClienteAppId ClienteAppId { get; private set; }

    public string IdempotencyKey { get; private set; }
    public TipoDocumento Tipo { get; private set; }
    public ChaveAcesso ChaveAcesso { get; private set; }
    public NfeDestinatario? NfeDestinatario { get; private set; }
    public string? NatOp { get; private set; }
    public int ModFrete { get; private set; } = 9;
    public long Numero { get; private set; }
    public string Serie { get; private set; }
    public int IndPresenca { get; private set; }
    public string? CpfConsumidor { get; private set; }
    public string? NomeConsumidor { get; private set; }
    public StatusDocumento Status { get; private set; }
    public string? XmlAssinado { get; private set; }
    public string? Protocolo { get; private set; }
    public QrCode? QrCode { get; private set; }
    public string? MotivoRejeicao { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset? AuthorizedAt { get; private set; }
    public DateTimeOffset? CanceladoAt { get; private set; }

    public IReadOnlyList<ItemDocumento> Items => _items.AsReadOnly();
    public IReadOnlyList<Pagamento> Pagamentos => _pagamentos.AsReadOnly();

    public static Result<DocumentoFiscal> Criar(
        DocumentoFiscalId id,
        TenantId tenantId,
        ClienteAppId clienteAppId,
        string idempotencyKey,
        TipoDocumento tipo,
        ChaveAcesso chaveAcesso,
        long numero,
        string serie,
        int indPresenca,
        IEnumerable<ItemDocumento> items,
        IEnumerable<Pagamento> pagamentos,
        TimeProvider timeProvider,
        string? cpfConsumidor = null,
        string? nomeConsumidor = null,
        NfeDestinatario? nfeDestinatario = null,
        string? natOp = null,
        int modFrete = 9)
    {
        if (tenantId == default)
            return Result.Failure<DocumentoFiscal>(DocumentoFiscalErrors.TenantInvalido);

        if (clienteAppId == default)
            return Result.Failure<DocumentoFiscal>(DocumentoFiscalErrors.ClienteAppInvalido);

        if (string.IsNullOrWhiteSpace(idempotencyKey))
            return Result.Failure<DocumentoFiscal>(DocumentoFiscalErrors.IdempotencyKeyInvalida);

        var indPresencaValidos = tipo == TipoDocumento.NFe ? IndPresencaNfe : IndPresencaNfce;
        if (!indPresencaValidos.Contains(indPresenca))
            return Result.Failure<DocumentoFiscal>(DocumentoFiscalErrors.IndPresencaInvalido);

        if (tipo == TipoDocumento.NFe)
        {
            if (nfeDestinatario is null)
                return Result.Failure<DocumentoFiscal>(DocumentoFiscalErrors.DestinatarioObrigatorioParaNfe);
            if (string.IsNullOrWhiteSpace(natOp))
                return Result.Failure<DocumentoFiscal>(DocumentoFiscalErrors.NatOpObrigatoriaNfe);
        }
        else if (nfeDestinatario is not null)
        {
            return Result.Failure<DocumentoFiscal>(DocumentoFiscalErrors.DestinatarioNaoPermitidoEmNfce);
        }

        if (cpfConsumidor is not null && string.IsNullOrWhiteSpace(nomeConsumidor))
            return Result.Failure<DocumentoFiscal>(DocumentoFiscalErrors.NomeConsumidorObrigatorio);

        var itemList = items.ToList();
        if (itemList.Count == 0)
            return Result.Failure<DocumentoFiscal>(DocumentoFiscalErrors.SemItens);

        var pagamentoList = pagamentos.ToList();

        return Result.Success(new DocumentoFiscal(
            id,
            tenantId,
            clienteAppId,
            idempotencyKey,
            tipo,
            chaveAcesso,
            numero,
            serie,
            indPresenca,
            cpfConsumidor,
            nomeConsumidor,
            itemList,
            pagamentoList,
            timeProvider.GetUtcNow(),
            nfeDestinatario,
            natOp,
            modFrete));
    }

    public Result Enfileirar()
    {
        if (Status != StatusDocumento.Criado)
            return Result.Failure(DocumentoFiscalErrors.TransicaoInvalida);

        Status = StatusDocumento.Enfileirado;
        return Result.Success();
    }

    public Result IniciarProcessamento()
    {
        if (Status != StatusDocumento.Enfileirado)
            return Result.Failure(DocumentoFiscalErrors.TransicaoInvalida);

        Status = StatusDocumento.Processando;
        return Result.Success();
    }

    public Result Autorizar(
        string protocolo,
        string xmlAssinado,
        QrCode qrCode,
        DateTimeOffset authorizedAt,
        TimeProvider timeProvider)
    {
        if (Status != StatusDocumento.Processando)
            return Result.Failure(DocumentoFiscalErrors.TransicaoInvalida);

        if (string.IsNullOrWhiteSpace(protocolo))
            return Result.Failure(DocumentoFiscalErrors.TransicaoInvalida);

        if (string.IsNullOrWhiteSpace(xmlAssinado))
            return Result.Failure(DocumentoFiscalErrors.TransicaoInvalida);

        Status = StatusDocumento.Autorizado;
        Protocolo = protocolo;
        XmlAssinado = xmlAssinado;
        QrCode = qrCode;
        AuthorizedAt = authorizedAt;

        AddDomainEvent(new DocumentoFiscalAutorizadoEvent(
            Id,
            TenantId,
            ClienteAppId,
            ChaveAcesso.Valor,
            protocolo,
            authorizedAt,
            Guid.CreateVersion7(),
            timeProvider.GetUtcNow()));

        return Result.Success();
    }

    public Result Rejeitar(string motivo, TimeProvider timeProvider)
    {
        if (Status != StatusDocumento.Processando)
            return Result.Failure(DocumentoFiscalErrors.TransicaoInvalida);

        Status = StatusDocumento.Rejeitado;
        MotivoRejeicao = motivo;

        AddDomainEvent(new DocumentoFiscalRejeitadoEvent(
            Id,
            TenantId,
            motivo,
            Guid.CreateVersion7(),
            timeProvider.GetUtcNow()));

        return Result.Success();
    }

    public Result IniciarCancelamento(TimeProvider timeProvider)
    {
        if (Status != StatusDocumento.Autorizado)
            return Result.Failure(DocumentoFiscalErrors.TransicaoInvalida);

        if (AuthorizedAt is null)
            return Result.Failure(DocumentoFiscalErrors.TransicaoInvalida);

        var utcNow = timeProvider.GetUtcNow();
        var prazo = Tipo switch
        {
            TipoDocumento.NFe  => AuthorizedAt.Value.AddHours(24),
            TipoDocumento.NfCe => AuthorizedAt.Value.AddMinutes(30),
            _                  => throw new InvalidOperationException(
                $"Prazo de cancelamento não definido para TipoDocumento {Tipo}.")
        };

        if (utcNow >= prazo)
            return Result.Failure(DocumentoFiscalErrors.PrazoDeCancelamentoExpirado);

        MotivoRejeicao = null;
        Status = StatusDocumento.Cancelando;
        return Result.Success();
    }

    public Result ConfirmarCancelamento(DateTimeOffset canceladoAt, TimeProvider timeProvider)
    {
        if (Status != StatusDocumento.Cancelando)
            return Result.Failure(DocumentoFiscalErrors.TransicaoInvalida);

        Status = StatusDocumento.Cancelado;
        CanceladoAt = canceladoAt;

        AddDomainEvent(new DocumentoFiscalCanceladoEvent(
            Id,
            TenantId,
            canceladoAt,
            Guid.CreateVersion7(),
            timeProvider.GetUtcNow()));

        return Result.Success();
    }

    public Result RejeitarCancelamento(string motivo)
    {
        if (Status != StatusDocumento.Cancelando)
            return Result.Failure(DocumentoFiscalErrors.TransicaoInvalida);

        if (string.IsNullOrWhiteSpace(motivo))
            return Result.Failure(DocumentoFiscalErrors.TransicaoInvalida);

        Status = StatusDocumento.Autorizado;
        MotivoRejeicao = motivo;
        return Result.Success();
    }

    public Result Falhar(TimeProvider timeProvider)
    {
        if (Status != StatusDocumento.Processando)
            return Result.Failure(DocumentoFiscalErrors.TransicaoInvalida);

        Status = StatusDocumento.Falhou;

        var utcNow = timeProvider.GetUtcNow();
        AddDomainEvent(new DocumentoFiscalFalhouEvent(
            Id,
            TenantId,
            utcNow,
            Guid.CreateVersion7(),
            utcNow));

        return Result.Success();
    }

    // cnpjEmitente: necessário para log Critical no DocumentoFiscalDenegadoEventHandler (decisions.md DA-11).
    // Passado pelo handler que já possui o CNPJ do Tenant sem query adicional.
    public Result Denegar(string motivo, string cnpjEmitente, TimeProvider timeProvider)
    {
        if (Status != StatusDocumento.Processando)
            return Result.Failure(DocumentoFiscalErrors.TransicaoInvalida);

        Status = StatusDocumento.Denegado;
        MotivoRejeicao = motivo;

        AddDomainEvent(new DocumentoFiscalDenegadoEvent(
            Id,
            TenantId,
            cnpjEmitente,
            motivo,
            Guid.CreateVersion7(),
            timeProvider.GetUtcNow()));

        return Result.Success();
    }
}

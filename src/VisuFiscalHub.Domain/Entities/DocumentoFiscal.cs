using VisuFiscalHub.Domain.Common;
using VisuFiscalHub.Domain.Enums;
using VisuFiscalHub.Domain.Errors;
using VisuFiscalHub.Domain.Events;
using VisuFiscalHub.Domain.Identifiers;
using VisuFiscalHub.Domain.ValueObjects;

namespace VisuFiscalHub.Domain.Entities;

public sealed class DocumentoFiscal : Entity<DocumentoFiscalId>
{
    // Valores válidos para NFC-e per SEFAZ NT 2019.001. O valor 2 (internet) é rejeitado.
    private static readonly IReadOnlySet<int> IndPresencaValidos = new HashSet<int> { 1, 3, 4, 9 };

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
        List<ItemDocumento> items,
        List<Pagamento> pagamentos,
        DateTimeOffset createdAt) : base(id)
    {
        TenantId = tenantId;
        ClienteAppId = clienteAppId;
        IdempotencyKey = idempotencyKey;
        Tipo = tipo;
        ChaveAcesso = chaveAcesso;
        Numero = numero;
        Serie = serie;
        IndPresenca = indPresenca;
        Status = StatusDocumento.Criado;
        _items = items;
        _pagamentos = pagamentos;
        CreatedAt = createdAt;
    }

    public TenantId TenantId { get; private set; }

    // Armazenado para evitar query adicional ao publicar DocumentoFiscalAutorizadoEvent/DenegadoEvent
    public ClienteAppId ClienteAppId { get; private set; }

    public string IdempotencyKey { get; private set; }
    public TipoDocumento Tipo { get; private set; }
    public ChaveAcesso ChaveAcesso { get; private set; }
    public long Numero { get; private set; }
    public string Serie { get; private set; }
    public int IndPresenca { get; private set; }
    public StatusDocumento Status { get; private set; }
    public string? XmlAssinado { get; private set; }
    public string? Protocolo { get; private set; }
    public QrCode? QrCode { get; private set; }
    public string? MotivoRejeicao { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset? AuthorizedAt { get; private set; }

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
        TimeProvider timeProvider)
    {
        if (tenantId == default)
            return Result.Failure<DocumentoFiscal>(DocumentoFiscalErrors.TenantInvalido);

        if (clienteAppId == default)
            return Result.Failure<DocumentoFiscal>(DocumentoFiscalErrors.ClienteAppInvalido);

        if (string.IsNullOrWhiteSpace(idempotencyKey))
            return Result.Failure<DocumentoFiscal>(DocumentoFiscalErrors.IdempotencyKeyInvalida);

        if (!IndPresencaValidos.Contains(indPresenca))
            return Result.Failure<DocumentoFiscal>(DocumentoFiscalErrors.IndPresencaInvalido);

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
            itemList,
            pagamentoList,
            timeProvider.GetUtcNow()));
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

    // Cancela o documento se ainda dentro do prazo de 30 minutos após autorização.
    // Boundary estrito: AuthorizedAt + 30min == utcNow → NÃO pode cancelar (usa <, não <=).
    public Result Cancelar(TimeProvider timeProvider)
    {
        if (Status != StatusDocumento.Autorizado)
            return Result.Failure(DocumentoFiscalErrors.TransicaoInvalida);

        var utcNow = timeProvider.GetUtcNow();
        var prazoLimite = AuthorizedAt!.Value.AddMinutes(30);

        if (utcNow >= prazoLimite)
            return Result.Failure(DocumentoFiscalErrors.PrazoDeCancelamentoExpirado);

        Status = StatusDocumento.Cancelado;

        AddDomainEvent(new DocumentoFiscalCanceladoEvent(
            Id,
            TenantId,
            utcNow,
            Guid.CreateVersion7(),
            utcNow));

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

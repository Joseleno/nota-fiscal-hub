using System.Security.Cryptography;
using Mediator;
using VisuFiscalHub.Application.Common.Interfaces;
using VisuFiscalHub.Application.Common.Models;
using VisuFiscalHub.Application.Common.Security;
using VisuFiscalHub.Domain.Common;
using VisuFiscalHub.Domain.Entities;
using VisuFiscalHub.Domain.Errors;
using VisuFiscalHub.Domain.Interfaces;

namespace VisuFiscalHub.Application.Tenants.Commands.CreateClienteApp;

public sealed class CreateClienteAppCommandHandler
    : ICommandHandler<CreateClienteAppCommand, Result<ClienteAppCreatedResponse>>
{
    private readonly IClienteAppRepository _clienteAppRepository;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ICertificateEncryptionService _encryptionService;
    private readonly TimeProvider _timeProvider;

    public CreateClienteAppCommandHandler(
        IClienteAppRepository clienteAppRepository,
        IUnitOfWork unitOfWork,
        ICertificateEncryptionService encryptionService,
        TimeProvider timeProvider)
    {
        _clienteAppRepository = clienteAppRepository;
        _unitOfWork = unitOfWork;
        _encryptionService = encryptionService;
        _timeProvider = timeProvider;
    }

    public async ValueTask<Result<ClienteAppCreatedResponse>> Handle(
        CreateClienteAppCommand command,
        CancellationToken cancellationToken)
    {
        var existing = await _clienteAppRepository.GetByClientIdAsync(command.ClientId, cancellationToken);
        if (existing is not null)
            return Result.Failure<ClienteAppCreatedResponse>(ClienteAppErrors.ClientIdJaExiste);

        var clientSecretHash = ClientSecretHasher.DerivarHash(command.ClientSecret);

        var webhookSecretHex = GerarWebhookSecretHex();
        var encryptResult = _encryptionService.EncryptString(webhookSecretHex);
        if (encryptResult.IsFailure)
            return Result.Failure<ClienteAppCreatedResponse>(encryptResult.Error);

        var createResult = ClienteApp.Criar(
            command.Name,
            command.ClientId,
            clientSecretHash,
            _timeProvider,
            command.WebhookUrl,
            encryptResult.Value);

        if (createResult.IsFailure)
            return Result.Failure<ClienteAppCreatedResponse>(createResult.Error);

        var clienteApp = createResult.Value;

        await _clienteAppRepository.AddAsync(clienteApp, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success(new ClienteAppCreatedResponse(
            clienteApp.Id.Value,
            clienteApp.Name,
            clienteApp.ClientId,
            clienteApp.WebhookUrl,
            webhookSecretHex,
            clienteApp.IsActive,
            clienteApp.CreatedAt));
    }

    private static string GerarWebhookSecretHex()
        => Convert.ToHexString(RandomNumberGenerator.GetBytes(32)).ToLowerInvariant();
}

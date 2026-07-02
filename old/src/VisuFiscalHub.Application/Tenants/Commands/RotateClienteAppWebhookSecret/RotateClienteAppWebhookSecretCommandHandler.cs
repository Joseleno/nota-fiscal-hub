using System.Security.Cryptography;
using Mediator;
using VisuFiscalHub.Application.Common.Interfaces;
using VisuFiscalHub.Application.Common.Models;
using VisuFiscalHub.Domain.Common;
using VisuFiscalHub.Domain.Errors;
using VisuFiscalHub.Domain.Interfaces;

namespace VisuFiscalHub.Application.Tenants.Commands.RotateClienteAppWebhookSecret;

public sealed class RotateClienteAppWebhookSecretCommandHandler
    : ICommandHandler<RotateClienteAppWebhookSecretCommand, Result<RotateClienteAppWebhookSecretResponse>>
{
    private readonly IClienteAppRepository _clienteAppRepository;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ICertificateEncryptionService _encryptionService;
    private readonly TimeProvider _timeProvider;

    public RotateClienteAppWebhookSecretCommandHandler(
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

    public async ValueTask<Result<RotateClienteAppWebhookSecretResponse>> Handle(
        RotateClienteAppWebhookSecretCommand command,
        CancellationToken cancellationToken)
    {
        var clienteApp = await _clienteAppRepository.GetByIdAsync(command.ClienteAppId, cancellationToken);
        if (clienteApp is null)
            return Result.Failure<RotateClienteAppWebhookSecretResponse>(ClienteAppErrors.NaoEncontrado);

        var newWebhookSecretHex = Convert.ToHexString(RandomNumberGenerator.GetBytes(32)).ToLowerInvariant();
        var encryptResult = _encryptionService.EncryptString(newWebhookSecretHex);
        if (encryptResult.IsFailure)
            return Result.Failure<RotateClienteAppWebhookSecretResponse>(encryptResult.Error);

        var updateResult = clienteApp.AtualizarWebhookSecret(encryptResult.Value, _timeProvider);
        if (updateResult.IsFailure)
            return Result.Failure<RotateClienteAppWebhookSecretResponse>(updateResult.Error);

        await _clienteAppRepository.UpdateAsync(clienteApp, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success(new RotateClienteAppWebhookSecretResponse(
            clienteApp.ClientId,
            newWebhookSecretHex));
    }
}

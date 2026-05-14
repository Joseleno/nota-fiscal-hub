using System.Security.Cryptography;
using Mediator;
using VisuFiscalHub.Application.Common.Models;
using VisuFiscalHub.Application.Common.Security;
using VisuFiscalHub.Domain.Common;
using VisuFiscalHub.Domain.Errors;
using VisuFiscalHub.Domain.Interfaces;

namespace VisuFiscalHub.Application.Tenants.Commands.RotateClienteAppSecret;

public sealed class RotateClienteAppSecretCommandHandler
    : ICommandHandler<RotateClienteAppSecretCommand, Result<RotateClienteAppSecretResponse>>
{
    private readonly IClienteAppRepository _clienteAppRepository;
    private readonly IUnitOfWork _unitOfWork;

    public RotateClienteAppSecretCommandHandler(
        IClienteAppRepository clienteAppRepository,
        IUnitOfWork unitOfWork)
    {
        _clienteAppRepository = clienteAppRepository;
        _unitOfWork = unitOfWork;
    }

    public async ValueTask<Result<RotateClienteAppSecretResponse>> Handle(
        RotateClienteAppSecretCommand command,
        CancellationToken cancellationToken)
    {
        var clienteApp = await _clienteAppRepository.GetByIdAsync(command.ClienteAppId, cancellationToken);
        if (clienteApp is null)
            return Result.Failure<RotateClienteAppSecretResponse>(ClienteAppErrors.NaoEncontrado);

        var newSecret = Convert.ToHexString(RandomNumberGenerator.GetBytes(32)).ToLowerInvariant();
        var newHash = ClientSecretHasher.DerivarHash(newSecret);

        var updateResult = clienteApp.AtualizarClientSecretHash(newHash);
        if (updateResult.IsFailure)
            return Result.Failure<RotateClienteAppSecretResponse>(updateResult.Error);

        await _clienteAppRepository.UpdateAsync(clienteApp, cancellationToken);

        try
        {
            await _unitOfWork.SaveChangesAsync(cancellationToken);
        }
        catch (Exception) when (!cancellationToken.IsCancellationRequested)
        {
            return Result.Failure<RotateClienteAppSecretResponse>(ClienteAppErrors.ClientSecretHashInvalido);
        }

        return Result.Success(new RotateClienteAppSecretResponse(
            clienteApp.ClientId,
            newSecret));
    }
}

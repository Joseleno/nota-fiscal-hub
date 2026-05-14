using Mediator;
using VisuFiscalHub.Application.Common.Interfaces;
using VisuFiscalHub.Domain.Common;
using VisuFiscalHub.Domain.Errors;
using VisuFiscalHub.Domain.Interfaces;

namespace VisuFiscalHub.Application.Tenants.Commands.UpdateTenantCsc;

public sealed class UpdateTenantCscCommandHandler
    : ICommandHandler<UpdateTenantCscCommand, Result>
{
    private readonly ITenantRepository _tenantRepository;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ICertificateEncryptionService _encryptionService;

    public UpdateTenantCscCommandHandler(
        ITenantRepository tenantRepository,
        IUnitOfWork unitOfWork,
        ICertificateEncryptionService encryptionService)
    {
        _tenantRepository = tenantRepository;
        _unitOfWork = unitOfWork;
        _encryptionService = encryptionService;
    }

    public async ValueTask<Result> Handle(
        UpdateTenantCscCommand command,
        CancellationToken cancellationToken)
    {
        var tenant = await _tenantRepository.GetByIdAsync(command.TenantId, cancellationToken);
        if (tenant is null)
            return Result.Failure(TenantErrors.NaoEncontrado);

        if (tenant.ClienteAppId != command.ClienteAppId)
            return Result.Failure(TenantErrors.NaoPertenceAoClienteApp);

        var cscResult = _encryptionService.EncryptString(command.Csc);
        if (cscResult.IsFailure)
            return Result.Failure(cscResult.Error);

        var updateResult = tenant.AtualizarCsc(cscResult.Value, command.CIdToken);
        if (updateResult.IsFailure)
            return updateResult;

        await _tenantRepository.UpdateAsync(tenant, cancellationToken);

        try
        {
            await _unitOfWork.SaveChangesAsync(cancellationToken);
        }
        catch (Exception) when (!cancellationToken.IsCancellationRequested)
        {
            return Result.Failure(TenantErrors.CscInvalido);
        }

        return Result.Success();
    }
}

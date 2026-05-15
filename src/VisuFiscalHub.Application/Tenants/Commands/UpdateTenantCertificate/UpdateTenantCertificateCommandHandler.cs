using Mediator;
using VisuFiscalHub.Application.Common.Interfaces;
using VisuFiscalHub.Domain.Common;
using VisuFiscalHub.Domain.Errors;
using VisuFiscalHub.Domain.Interfaces;

namespace VisuFiscalHub.Application.Tenants.Commands.UpdateTenantCertificate;

public sealed class UpdateTenantCertificateCommandHandler
    : ICommandHandler<UpdateTenantCertificateCommand, Result>
{
    private readonly ITenantRepository _tenantRepository;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ICertificateEncryptionService _encryptionService;
    private readonly TimeProvider _timeProvider;

    public UpdateTenantCertificateCommandHandler(
        ITenantRepository tenantRepository,
        IUnitOfWork unitOfWork,
        ICertificateEncryptionService encryptionService,
        TimeProvider timeProvider)
    {
        _tenantRepository = tenantRepository;
        _unitOfWork = unitOfWork;
        _encryptionService = encryptionService;
        _timeProvider = timeProvider;
    }

    public async ValueTask<Result> Handle(
        UpdateTenantCertificateCommand command,
        CancellationToken cancellationToken)
    {
        var tenant = await _tenantRepository.GetByIdForUpdateAsync(command.TenantId, cancellationToken);
        if (tenant is null)
            return Result.Failure(TenantErrors.NaoEncontrado);

        if (tenant.ClienteAppId != command.ClienteAppId)
            return Result.Failure(TenantErrors.NaoPertenceAoClienteApp);

        var pfxResult = _encryptionService.Encrypt(command.PfxBytes);
        if (pfxResult.IsFailure)
            return Result.Failure(pfxResult.Error);

        var senhaResult = _encryptionService.EncryptString(command.Senha);
        if (senhaResult.IsFailure)
            return Result.Failure(senhaResult.Error);

        var updateResult = tenant.AtualizarCertificado(
            pfxResult.Value,
            senhaResult.Value,
            command.Vencimento,
            _timeProvider);

        if (updateResult.IsFailure)
            return updateResult;

        await _tenantRepository.UpdateAsync(tenant, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success();
    }
}

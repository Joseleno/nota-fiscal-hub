using Mediator;
using VisuFiscalHub.Application.Common;
using VisuFiscalHub.Application.Common.Interfaces;
using VisuFiscalHub.Application.Common.Models;
using VisuFiscalHub.Application.Tenants;
using VisuFiscalHub.Domain.Common;
using VisuFiscalHub.Domain.Entities;
using VisuFiscalHub.Domain.Errors;
using VisuFiscalHub.Domain.Interfaces;
using VisuFiscalHub.Domain.ValueObjects;

namespace VisuFiscalHub.Application.Tenants.Commands.CreateTenant;

public sealed class CreateTenantCommandHandler
    : ICommandHandler<CreateTenantCommand, Result<TenantResponse>>
{
    private readonly ITenantRepository _tenantRepository;
    private readonly IClienteAppRepository _clienteAppRepository;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ISequenceManager _sequenceManager;
    private readonly TimeProvider _timeProvider;

    public CreateTenantCommandHandler(
        ITenantRepository tenantRepository,
        IClienteAppRepository clienteAppRepository,
        IUnitOfWork unitOfWork,
        ISequenceManager sequenceManager,
        TimeProvider timeProvider)
    {
        _tenantRepository = tenantRepository;
        _clienteAppRepository = clienteAppRepository;
        _unitOfWork = unitOfWork;
        _sequenceManager = sequenceManager;
        _timeProvider = timeProvider;
    }

    public async ValueTask<Result<TenantResponse>> Handle(
        CreateTenantCommand command,
        CancellationToken cancellationToken)
    {
        if (!FiscalConstants.SerieRegex.IsMatch(command.Serie))
            return Result.Failure<TenantResponse>(TenantErrors.ConfiguracaoFiscalInvalida);

        var clienteApp = await _clienteAppRepository.GetByIdAsync(command.ClienteAppId, cancellationToken);
        if (clienteApp is null)
            return Result.Failure<TenantResponse>(ClienteAppErrors.NaoEncontrado);

        if (!clienteApp.IsActive)
            return Result.Failure<TenantResponse>(ClienteAppErrors.Inativo);

        var cnpjResult = Cnpj.Criar(command.Cnpj);
        if (cnpjResult.IsFailure)
            return Result.Failure<TenantResponse>(cnpjResult.Error);

        var cnpj = cnpjResult.Value;

        var configResult = ConfiguracaoFiscal.Criar(
            command.RegimeTributario,
            command.Serie,
            command.Ambiente,
            command.UfCodigo,
            command.InscricaoEstadual,
            serieNfe: command.SerieNfe);

        if (configResult.IsFailure)
            return Result.Failure<TenantResponse>(configResult.Error);

        var endereco = command.Endereco;
        var enderecoResult = Endereco.Criar(
            endereco.Logradouro,
            endereco.Numero,
            endereco.Complemento,
            endereco.Bairro,
            endereco.Municipio,
            endereco.CodigoMunicipio,
            endereco.Uf,
            endereco.Cep,
            endereco.CodigoPais,
            endereco.Telefone);

        if (enderecoResult.IsFailure)
            return Result.Failure<TenantResponse>(enderecoResult.Error);

        var tenantResult = Tenant.Criar(
            command.ClienteAppId,
            cnpj,
            command.RazaoSocial,
            command.NomeFantasia,
            configResult.Value,
            enderecoResult.Value,
            _timeProvider);

        if (tenantResult.IsFailure)
            return Result.Failure<TenantResponse>(tenantResult.Error);

        var tenant = tenantResult.Value;

        // Check de CNPJ e insert dentro da mesma transação eliminam race condition.
        // Violação de unique constraint é tratada no UnitOfWork e retornada como CnpjJaCadastrado.
        var transactionResult = await _unitOfWork.ExecuteInTransactionAsync(async ct =>
        {
            var existente = await _tenantRepository.GetByCnpjAsync(cnpj, command.ClienteAppId, ct);
            if (existente is not null)
                return Result.Failure(TenantErrors.CnpjJaCadastrado);

            await _tenantRepository.AddAsync(tenant, ct);
            await _unitOfWork.SaveChangesAsync(ct);
            var seqResult = await _sequenceManager.EnsureNumeracaoSequenceAsync(tenant.Id, command.Serie, ct);
            if (seqResult.IsFailure) return seqResult;

            if (command.SerieNfe is not null)
            {
                var seqNfeResult = await _sequenceManager.EnsureNumeracaoSequenceAsync(
                    tenant.Id, command.SerieNfe, ct);
                if (seqNfeResult.IsFailure) return seqNfeResult;
            }

            return seqResult;
        }, cancellationToken);

        if (transactionResult.IsFailure)
            return Result.Failure<TenantResponse>(transactionResult.Error);

        return Result.Success(TenantMapper.ToResponse(tenant));
    }
}

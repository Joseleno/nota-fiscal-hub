using Mediator;
using VisuFiscalHub.Application.Common.Models;
using VisuFiscalHub.Domain.Common;
using VisuFiscalHub.Domain.Enums;
using VisuFiscalHub.Domain.Identifiers;

namespace VisuFiscalHub.Application.Tenants.Commands.CreateTenant;

public sealed record CreateTenantCommand : ICommand<Result<TenantResponse>>
{
    public ClienteAppId ClienteAppId { get; init; }
    public string Cnpj { get; init; } = default!;
    public string RazaoSocial { get; init; } = default!;
    public string? NomeFantasia { get; init; }
    public RegimeTributario RegimeTributario { get; init; }
    public AmbienteSefaz Ambiente { get; init; }
    public int UfCodigo { get; init; }
    public string Serie { get; init; } = default!;
    public EnderecoDto Endereco { get; init; } = default!;
}

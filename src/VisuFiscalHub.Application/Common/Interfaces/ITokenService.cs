using VisuFiscalHub.Domain.Entities;

namespace VisuFiscalHub.Application.Common.Interfaces;

public interface ITokenService
{
    string GenerateToken(ClienteApp clienteApp);
}

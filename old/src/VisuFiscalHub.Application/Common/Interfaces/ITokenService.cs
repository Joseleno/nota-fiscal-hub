using VisuFiscalHub.Domain.Identifiers;

namespace VisuFiscalHub.Application.Common.Interfaces;

public interface ITokenService
{
    string GenerateToken(ClienteAppId clienteAppId, string clientId);
}

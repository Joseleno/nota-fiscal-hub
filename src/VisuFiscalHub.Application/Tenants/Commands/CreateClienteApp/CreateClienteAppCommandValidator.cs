using System.Net;
using FluentValidation;

namespace VisuFiscalHub.Application.Tenants.Commands.CreateClienteApp;

public sealed class CreateClienteAppCommandValidator
    : AbstractValidator<CreateClienteAppCommand>
{
    public CreateClienteAppCommandValidator()
    {
        RuleFor(x => x.Name)
            .NotEmpty().WithMessage("Nome do ClienteApp é obrigatório")
            .MaximumLength(100);

        RuleFor(x => x.ClientId)
            .NotEmpty().WithMessage("client_id é obrigatório")
            .Matches(@"^[a-z0-9\-_]{3,50}$")
            .WithMessage("client_id deve conter apenas letras minúsculas, números, hífens e underscores (3–50 chars)");

        RuleFor(x => x.ClientSecret)
            .NotEmpty().WithMessage("client_secret é obrigatório")
            .MinimumLength(16).WithMessage("client_secret deve ter no mínimo 16 caracteres");

        When(x => x.WebhookUrl is not null, () =>
        {
            RuleFor(x => x.WebhookUrl!)
                .Must(BeValidHttpsUrl)
                .WithMessage("webhookUrl deve ser HTTPS e não pode apontar para endereços privados (RFC 1918)");
        });
    }

    private static bool BeValidHttpsUrl(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
            return false;

        if (!uri.Scheme.Equals("https", StringComparison.OrdinalIgnoreCase))
            return false;

        if (uri.IsLoopback)
            return false;

        var host = uri.Host;
        if (!IPAddress.TryParse(host, out var ip))
        {
            // Hostname: cannot block private DNS without resolution — document limitation
            return true;
        }

        if (IPAddress.IsLoopback(ip))
            return false;

        var bytes = ip.GetAddressBytes();

        if (ip.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
        {
            // 0.0.0.0 — any interface
            if (ip.Equals(IPAddress.Any)) return false;
            // 10.x.x.x — RFC 1918
            if (bytes[0] == 10) return false;
            // 172.16.x.x – 172.31.x.x — RFC 1918
            if (bytes[0] == 172 && bytes[1] >= 16 && bytes[1] <= 31) return false;
            // 192.168.x.x — RFC 1918
            if (bytes[0] == 192 && bytes[1] == 168) return false;
            // 169.254.x.x — link-local
            if (bytes[0] == 169 && bytes[1] == 254) return false;
        }

        if (ip.AddressFamily == System.Net.Sockets.AddressFamily.InterNetworkV6)
        {
            // fc00::/7 — ULA (unique local addresses)
            if ((bytes[0] & 0xFE) == 0xFC) return false;
            // fe80::/10 — link-local
            if (bytes[0] == 0xFE && (bytes[1] & 0xC0) == 0x80) return false;
        }

        return true;
    }
}

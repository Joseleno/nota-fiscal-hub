namespace VisuFiscalHub.Application.Common.Models;

public sealed class HangfireDashboardSettings
{
    public const string SectionName = "HangfireDashboard";

    public string User { get; init; } = string.Empty;
    public string Password { get; init; } = string.Empty;
}

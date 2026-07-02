namespace VisuFiscalHub.Application.Common.Models;

public sealed class AdminKeySettings
{
    public const string SectionName = "AdminKey";

    public string Value { get; init; } = string.Empty;
}

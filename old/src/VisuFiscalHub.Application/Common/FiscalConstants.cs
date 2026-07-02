using System.Text.RegularExpressions;

namespace VisuFiscalHub.Application.Common;

public static class FiscalConstants
{
    public static readonly Regex SerieRegex = new(@"^[0-9]{1,3}$", RegexOptions.Compiled);
}

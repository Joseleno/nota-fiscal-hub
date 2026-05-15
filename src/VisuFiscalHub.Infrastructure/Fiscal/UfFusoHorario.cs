namespace VisuFiscalHub.Infrastructure.Fiscal;

// Mapa de fuso horário por código IBGE de UF (offsets fixos — Brasil não observa DST desde 2019).
// Fonte: decisions.md seção 5.1
public static class UfFusoHorario
{
    public static readonly IReadOnlyDictionary<int, TimeSpan> Mapa =
        new Dictionary<int, TimeSpan>
        {
            // UTC-3: maioria dos estados
            [11] = TimeSpan.FromHours(-4), // RO — UTC-4
            [12] = TimeSpan.FromHours(-5), // AC — UTC-5
            [13] = TimeSpan.FromHours(-4), // AM — UTC-4
            [14] = TimeSpan.FromHours(-4), // RR — UTC-4
            [15] = TimeSpan.FromHours(-3), // PA
            [16] = TimeSpan.FromHours(-3), // AP
            [17] = TimeSpan.FromHours(-3), // TO
            [21] = TimeSpan.FromHours(-3), // MA
            [22] = TimeSpan.FromHours(-3), // PI
            [23] = TimeSpan.FromHours(-3), // CE
            [24] = TimeSpan.FromHours(-3), // RN
            [25] = TimeSpan.FromHours(-3), // PB
            [26] = TimeSpan.FromHours(-3), // PE
            [27] = TimeSpan.FromHours(-3), // AL
            [28] = TimeSpan.FromHours(-3), // SE
            [29] = TimeSpan.FromHours(-3), // BA
            [31] = TimeSpan.FromHours(-3), // MG
            [32] = TimeSpan.FromHours(-3), // ES
            [33] = TimeSpan.FromHours(-3), // RJ
            [35] = TimeSpan.FromHours(-3), // SP
            [41] = TimeSpan.FromHours(-3), // PR
            [42] = TimeSpan.FromHours(-3), // SC
            [43] = TimeSpan.FromHours(-3), // RS
            [50] = TimeSpan.FromHours(-4), // MS — UTC-4
            [51] = TimeSpan.FromHours(-4), // MT — UTC-4
            [52] = TimeSpan.FromHours(-3), // GO
            [53] = TimeSpan.FromHours(-3), // DF
        };
}

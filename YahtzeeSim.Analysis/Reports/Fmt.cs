using System.Globalization;
using YahtzeeSim.Analysis.Grading;

namespace YahtzeeSim.Analysis.Reports;

/// <summary>Invariant, fixed-format text helpers shared by every report, so output never depends on the machine's culture.</summary>
internal static class Fmt
{
    public static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    public static string F0(double v) => v.ToString("0", Inv);

    public static string F1(double v) => v.ToString("0.0", Inv);

    public static string Signed(double v) => (v >= 0 ? "+" : "−") + Math.Abs(v).ToString("0.0", Inv);

    public static string Pct(double v) => v.ToString("0%", Inv);

    public static string I(int v) => v.ToString(Inv);

    public static string Date(long unixMs) => DateTimeOffset.FromUnixTimeMilliseconds(unixMs).UtcDateTime.ToString("yyyy-MM-dd", Inv);

    public static string Seconds(double s) => s < 60 ? s.ToString("0.0", Inv) + " s" : (s / 60).ToString("0.0", Inv) + " min";

    public static string Kind(DecisionKind kind) => kind switch
    {
        DecisionKind.KeepAfterRoll1 => "Keep after roll 1",
        DecisionKind.KeepAfterRoll2 => "Keep after roll 2",
        DecisionKind.StopAfterRoll1 => "Scored after roll 1",
        DecisionKind.StopAfterRoll2 => "Scored after roll 2",
        _ => "Box choice"
    };
}

using System.Globalization;
using System.Net;
using System.Text;
using YahtzeeSim.Analysis.Story;

namespace YahtzeeSim.Analysis.Reports;

/// <summary>Research view of the badge rules: each badge's metric for a player against its threshold and the reference strategy.</summary>
public static class BadgeReport
{
    public static string PlayerFragment(string nickname, StyleMetrics metrics, StyleMetrics? reference)
    {
        var result = BadgeRules.Choose(metrics);
        var h = new StringBuilder("<style>").Append(Css).Append("</style><div class=\"br\">");
        h.Append("<h2>").Append(E(nickname)).Append(": ").Append(E(result.Badge.Name)).Append("</h2>");
        h.Append("<p class=\"muted\">Badge rules v").Append(BadgeRules.Version).Append(". The badge with the greatest strength wins; strength = distance past the threshold ÷ scale; ties go to the earlier badge; none qualifying gives All-Rounder. ")
            .Append(I(metrics.Games)).Append(" game(s), ").Append(I(metrics.Turns)).Append(" turns, ").Append(I(metrics.ChoiceRolls)).Append(" choice rolls (four in a row, or a pair with three in a row).</p>");
        h.Append("<table><thead><tr><th>Badge</th><th>Rule</th><th class=\"num\">Player</th><th class=\"num\">Reference</th><th class=\"num\">Chances</th><th class=\"num\">Strength</th></tr></thead><tbody>");
        foreach (var score in result.Scores.OrderByDescending(s => s.Strength))
        {
            var b = score.Badge;
            string css = b == result.Badge ? "win" : score.Qualifies ? "ok" : "";
            h.Append("<tr class=\"").Append(css).Append("\"><td><strong>").Append(E(b.Name)).Append("</strong></td><td>").Append(E(b.Rule)).Append("</td><td class=\"num\">")
                .Append(E(b.Format(score.Value))).Append("</td><td class=\"num\">")
                .Append(reference == null || b.Timed ? "—" : E(b.Format(b.Metric(reference)))).Append("</td><td class=\"num\">")
                .Append(I(score.Opportunities)).Append("</td><td class=\"num\">")
                .Append(double.IsNegativeInfinity(score.Strength) ? "too few" : score.Strength.ToString("0.00", CultureInfo.InvariantCulture)).Append("</td></tr>");
        }

        h.Append("</tbody></table>");
        h.Append("<h3>Raw metrics</h3><table><tbody>");
        Row("Kept matching dice on choice rolls", $"{I(metrics.KeptMatching)} of {I(metrics.ChoiceRolls)} (reference strategy would: {metrics.ReferenceMatching.ToString("0.0", CultureInfo.InvariantCulture)})");
        Row("Kept the straight draw on choice rolls", $"{I(metrics.KeptStraight)} of {I(metrics.ChoiceRolls)} (reference strategy would: {metrics.ReferenceStraight.ToString("0.0", CultureInfo.InvariantCulture)})");
        Row("Upper bonus", $"{I(metrics.UpperBonuses)} of {I(metrics.Cards)} cards");
        Row("Cards in progress (average)", metrics.CardsInProgress.ToString("0.00", CultureInfo.InvariantCulture));
        Row("Turns scored before roll 3", $"{I(metrics.EarlyStops)} of {I(metrics.Turns)}");
        Row("Median turn time", metrics.MedianTurnSeconds.ToString("0.0", CultureInfo.InvariantCulture) + " s");
        Row("Yahtzees scored", I(metrics.YahtzeesScored));
        h.Append("</tbody></table></div>");
        return h.ToString();

        void Row(string label, string value) => h.Append("<tr><td>").Append(E(label)).Append("</td><td>").Append(E(value)).Append("</td></tr>");
    }

    private static string I(int v) => v.ToString(CultureInfo.InvariantCulture);

    private static string E(string text) => WebUtility.HtmlEncode(text);

    private const string Css = """
.br{color:#1f2933;font:14px/1.5 "Segoe UI",sans-serif;max-width:1000px}
.br h2{font-family:Georgia,serif;color:#17352c;margin:0 0 4px}.br h3{font-family:Georgia,serif;color:#17352c;margin:18px 0 4px}
.br .muted{color:#56645c}
.br table{border-collapse:collapse;width:100%;margin:8px 0}
.br th{text-align:left;font-size:.75rem;text-transform:uppercase;letter-spacing:.05em;color:#56645c;border-bottom:2px solid #dedfd5;padding:6px 8px}
.br td{border-bottom:1px solid #dedfd5;padding:6px 8px;vertical-align:top}
.br .num{text-align:right;white-space:nowrap;font-variant-numeric:tabular-nums}
.br tr.win td{background:#b8efc7}.br tr.ok td{background:#eef8f0}
""";
}

using System.Net;
using System.Text;
using YahtzeeSim.Analysis.Story;

namespace YahtzeeSim.Analysis.Reports;

/// <summary>
/// Renders a <see cref="PlayStory"/> as a short HTML fragment (styles scoped under <c>.ps</c>, no scripts or links).
/// The app and the research desktop app show exactly this markup.
/// </summary>
public static class StoryReport
{
    /// <summary>A standalone page around <see cref="Fragment"/>, for the script output.</summary>
    public static string Page(PlayStory story) =>
        "<!doctype html><html lang=\"en\"><head><meta charset=\"utf-8\"><meta name=\"viewport\" content=\"width=device-width, initial-scale=1\"><meta name=\"robots\" content=\"noindex\"><title>"
        + E(story.Nickname) + " · play style</title><style>body{margin:0;padding:20px 12px;background:#f1efe6}</style></head><body>"
        + Fragment(story) + "</body></html>\n";

    public static string Fragment(PlayStory story)
    {
        var h = new StringBuilder("<style>").Append(Css).Append("</style><div class=\"ps\">");

        h.Append("<section class=\"ps-badge\"><div class=\"ps-icon\">").Append(BadgeIcons.Svg(story.Badge.Id, 34)).Append("</div><div>")
            .Append("<p class=\"ps-kicker\">Your play style</p><h4>").Append(E(story.Badge.Name)).Append("</h4><p class=\"ps-desc\">")
            .Append(E(story.Badge.Description)).Append("</p><p>").Append(E(story.BadgeLine)).Append("</p></div></section>");

        if (story.RollLines.Count > 0)
        {
            h.Append("<section><h5>When your first roll shows…</h5><ul class=\"ps-rolls\">");
            foreach (var line in story.RollLines)
            {
                h.Append("<li><div class=\"ps-roll\"><strong>").Append(E(line.Lead)).Append("</strong>").Append(Dice(line.ExampleDice, line.ExampleKept))
                    .Append("</div><p>").Append(E(Capitalize(line.Text))).Append("</p></li>");
            }

            h.Append("</ul><p class=\"ps-legend\"><span class=\"ps-keep-key\"></span>Dice you kept in that example</p></section>");
        }

        h.Append("<section><h5>How you fill your sheet</h5><ul class=\"ps-list\">");
        foreach (string line in story.SheetLines)
        {
            h.Append("<li>").Append(E(line)).Append("</li>");
        }

        h.Append("</ul></section>");

        if (story.Highlights.Count > 0)
        {
            h.Append("<section><h5>Highlights</h5><ul class=\"ps-list ps-high\">");
            foreach (string line in story.Highlights)
            {
                h.Append("<li>").Append(E(line)).Append("</li>");
            }

            h.Append("</ul></section>");
        }

        if (story.Tip is { } tip)
        {
            h.Append("<section class=\"ps-tip\"><h5>One thing to try</h5><p>").Append(E(tip.Text)).Append("</p>");
            if (tip.Example != null)
            {
                h.Append("<div class=\"ps-example\">");
                if (tip.ExampleDice != null)
                {
                    h.Append(Dice([.. tip.ExampleDice.Select(c => c - '0')], []));
                }

                h.Append("<p>").Append(E(tip.Example)).Append("</p></div>");
            }

            h.Append("</section>");
        }

        h.Append("<p class=\"ps-foot\">Based on your last ").Append(story.Games).Append(" finished Full Sheet game").Append(story.Games == 1 ? "" : "s")
            .Append(". Every line comes from fixed rules and your own rolls.</p></div>");
        return h.ToString();
    }

    private static string Dice(int[] unsorted, int[] kept)
    {
        int[] dice = [.. unsorted.Order()];
        var remaining = kept.ToList();
        var sb = new StringBuilder("<span class=\"ps-dice\" role=\"img\" aria-label=\"Dice ").Append(string.Join(" ", dice));
        if (kept.Length > 0 && kept.Length < 5)
        {
            sb.Append(", kept ").Append(string.Join(" ", kept));
        }

        sb.Append("\">");
        foreach (int value in dice)
        {
            bool isKept = kept.Length is > 0 and < 5 && remaining.Remove(value);
            sb.Append(Die(value, isKept));
        }

        return sb.Append("</span>").ToString();
    }

    private static string Die(int value, bool kept)
    {
        int[][] pips = value switch
        {
            1 => [[2, 2]],
            2 => [[1, 1], [3, 3]],
            3 => [[1, 1], [2, 2], [3, 3]],
            4 => [[1, 1], [3, 1], [1, 3], [3, 3]],
            5 => [[1, 1], [3, 1], [2, 2], [1, 3], [3, 3]],
            _ => [[1, 1], [3, 1], [1, 2], [3, 2], [1, 3], [3, 3]]
        };
        var sb = new StringBuilder("<svg class=\"ps-die").Append(kept ? " kept" : "").Append("\" viewBox=\"0 0 24 24\" aria-hidden=\"true\"><rect x=\"1\" y=\"1\" width=\"22\" height=\"22\" rx=\"5\"/>");
        foreach (var p in pips)
        {
            sb.Append("<circle cx=\"").Append(p[0] * 6).Append("\" cy=\"").Append(p[1] * 6).Append("\" r=\"2.3\"/>");
        }

        return sb.Append("</svg>").ToString();
    }

    private static string Capitalize(string text) => text.Length == 0 ? text : char.ToUpperInvariant(text[0]) + text[1..];

    private static string E(string text) => WebUtility.HtmlEncode(text);

    private const string Css = """
.ps{--ink:#1f2933;--forest:#17352c;--muted:#56645c;--line:#dedfd5;--paper:#f8f6ee;--mint:#b8efc7;--mint-deep:#31966a;--gold:#f4c66a;--die:#fbf8ee;color:var(--ink);font:15px/1.5 "Trebuchet MS","Segoe UI",sans-serif;text-align:left;max-width:640px;margin:0 auto}
.ps *{box-sizing:border-box}
.ps section{background:var(--paper);border:1px solid var(--line);border-radius:14px;padding:14px 16px;margin:0 0 12px}
.ps h4{font-family:Georgia,"Times New Roman",serif;font-size:1.45rem;margin:0;color:var(--forest)}
.ps h5{font-family:Georgia,"Times New Roman",serif;font-size:1.05rem;margin:0 0 8px;color:var(--forest)}
.ps p{margin:4px 0}
.ps-badge{display:flex;gap:14px;align-items:center;background:linear-gradient(135deg,#102a25,#17352c 60%,#1f6f5f)!important;color:#fff;border:0!important}
.ps-badge h4{color:var(--gold)}
.ps-icon{flex:none;width:58px;height:58px;border-radius:50%;display:grid;place-items:center;background:rgba(244,198,106,.16);color:var(--gold);border:2px solid var(--gold)}
.ps-kicker{text-transform:uppercase;letter-spacing:.12em;font-size:.72rem;color:var(--mint);margin:0!important}
.ps-desc{color:#e7efe9;font-style:italic}
.ps-rolls,.ps-list{list-style:none;margin:0;padding:0}
.ps-rolls li{padding:8px 0;border-bottom:1px solid var(--line)}.ps-rolls li:last-child{border-bottom:0}
.ps-roll{display:flex;flex-wrap:wrap;align-items:center;gap:6px 10px}
.ps-dice{display:inline-flex;gap:3px}
.ps-die{width:24px;height:24px}.ps-die rect{fill:var(--die);stroke:var(--forest);stroke-width:1.4}.ps-die circle{fill:var(--forest)}
.ps-die.kept rect{fill:var(--mint);stroke:var(--mint-deep);stroke-width:2}
.ps-legend{font-size:.8rem;color:var(--muted)}.ps-keep-key{display:inline-block;width:12px;height:12px;border-radius:3px;background:var(--mint);border:1.5px solid var(--mint-deep);margin-right:6px;vertical-align:-2px}
.ps-list li{padding:4px 0 4px 18px;position:relative}.ps-list li::before{content:"";position:absolute;left:4px;top:.75em;width:6px;height:6px;border-radius:50%;background:var(--mint-deep)}
.ps-high li::before{background:var(--gold)}
.ps-tip{border-left:4px solid var(--gold)!important;background:#fff!important}
.ps-example{display:flex;flex-wrap:wrap;gap:6px 10px;align-items:center;margin-top:6px;font-size:.9rem;color:var(--muted)}
.ps-foot{font-size:.78rem;color:var(--muted);text-align:center}
""";
}

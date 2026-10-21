using System.Net;
using System.Text;
using YahtzeeSim.Analysis.Grading;
using YahtzeeSim.Analysis.Profile;
using YahtzeeSim.Analysis.Rules;
using YahtzeeSim.Analysis.Solver;
using YahtzeeSim.Engine;
using static YahtzeeSim.Analysis.Reports.Fmt;

namespace YahtzeeSim.Analysis.Reports;

/// <summary>
/// Self-contained HTML pages (no scripts, no external files) in the app's colours: an index page and one page per
/// player. Every sentence is a fixed label or a rule template; numbers come from the analysis.
/// </summary>
public static class HtmlReport
{
    public static string Index(AnalysisResult result)
    {
        var h = new StringBuilder();
        Start(h, "Full Sheet strategy report");
        h.Append("<header class=\"hero\"><div class=\"wrap\"><p class=\"eyebrow\">YahtzeeSim · Full Sheet</p><h1>Strategy report</h1>");
        h.Append("<p class=\"lede\">").Append(I(result.GameCount)).Append(" finished Full Sheet game").Append(result.GameCount == 1 ? "" : "s")
            .Append(" from ").Append(I(result.Players.Count)).Append(" player").Append(result.Players.Count == 1 ? "" : "s");
        if (result.FirstPlayedAt is { } first && result.LastPlayedAt is { } last)
        {
            h.Append(", played ").Append(Date(first)).Append(Date(first) == Date(last) ? "" : " to " + Date(last));
        }

        h.Append(". Rules v").Append(E(result.RulesVersion)).Append(".</p></div></header><main class=\"wrap\">");

        h.Append("<section><h2>Players</h2><div class=\"players\">");
        foreach (var p in result.Players)
        {
            var fired = p.Findings.Where(f => f.Fired).ToList();
            h.Append("<a class=\"player-card\" href=\"").Append(E(p.Slug)).Append(".html\"><h3>").Append(E(p.Nickname)).Append("</h3>");
            h.Append("<p class=\"muted\">").Append(I(p.Games.Count)).Append(" game").Append(p.Games.Count == 1 ? "" : "s").Append("</p>");
            h.Append("<dl class=\"mini\">");
            Mini(h, "Average score", F0(p.AverageTotal));
            Mini(h, "Best", I(p.Games.Max(g => g.Game.Total)));
            Mini(h, "Luck-free score", F0(p.AverageLuckFree));
            Mini(h, "Matches reference", Pct(p.MatchRate));
            h.Append("</dl>");
            if (fired.Count > 0)
            {
                h.Append("<div class=\"chips\">");
                foreach (var f in fired.Take(4))
                {
                    h.Append("<span class=\"chip\">").Append(E(f.Rule.Title)).Append("</span>");
                }

                h.Append("</div>");
            }
            else
            {
                h.Append("<p class=\"muted small\">No rules triggered.</p>");
            }

            h.Append("<span class=\"go\">Open report →</span></a>");
        }

        h.Append("</div></section>");

        Method(h, result);
        RulesTable(h);

        if (result.Skipped.Count > 0)
        {
            h.Append("<section class=\"panel\"><h2>Games not analysed</h2><table><thead><tr><th>Player</th><th>Game</th><th>Mode</th><th>Reason</th></tr></thead><tbody>");
            foreach (var s in result.Skipped)
            {
                h.Append("<tr><td>").Append(E(s.Nickname)).Append("</td><td><code>").Append(E(Short(s.GameId))).Append("</code></td><td>")
                    .Append(E(s.Mode)).Append("</td><td>").Append(E(s.Reason)).Append("</td></tr>");
            }

            h.Append("</tbody></table></section>");
        }

        End(h, result);
        return h.ToString();
    }

    public static string Player(AnalysisResult result, PlayerAnalysis p)
    {
        var h = new StringBuilder();
        Start(h, p.Nickname + " · Full Sheet strategy");
        h.Append("<header class=\"hero\"><div class=\"wrap\"><p class=\"eyebrow\"><a href=\"index.html\">← All players</a></p><h1>")
            .Append(E(p.Nickname)).Append("</h1><p class=\"lede\">");
        Summary(h, result, p);
        h.Append("</p></div></header><main class=\"wrap\">");
        PlayerSections(h, result, p);
        End(h, result);
        return h.ToString();
    }

    /// <summary>
    /// The player report as a fragment for the app: no page header or links, styles scoped under <c>.sr</c>,
    /// followed by how the report works and the rule table.
    /// </summary>
    public static string PlayerFragment(AnalysisResult result, PlayerAnalysis p)
    {
        var h = new StringBuilder("<style>").Append(Css).Append("</style><div class=\"sr sr-embedded\"><p class=\"intro\">");
        Summary(h, result, p);
        h.Append("</p>");
        PlayerSections(h, result, p);
        Method(h, result);
        RulesTable(h);
        h.Append("<footer>Rules v").Append(E(result.RulesVersion)).Append(" · reference from ").Append(I(result.ReferenceData.Simulations))
            .Append(" simulated games per strategy, seed ").Append(I(result.ReferenceData.Seed)).Append("</footer></div>");
        return h.ToString();
    }

    private static void Summary(StringBuilder h, AnalysisResult result, PlayerAnalysis p)
    {
        h.Append(I(p.Games.Count)).Append(" finished Full Sheet game").Append(p.Games.Count == 1 ? "" : "s").Append(", ").Append(Date(p.Games[0].Game.PlayedAt));
        if (p.Games.Count > 1 && Date(p.Games[^1].Game.PlayedAt) != Date(p.Games[0].Game.PlayedAt))
        {
            h.Append(" to ").Append(Date(p.Games[^1].Game.PlayedAt));
        }

        h.Append(". Compared with the reference strategy (").Append(F0(result.Reference.Mean)).Append(" average over ")
            .Append(I(result.Reference.Games)).Append(" simulated games).");
    }

    private static void PlayerSections(StringBuilder h, AnalysisResult result, PlayerAnalysis p)
    {
        var reference = result.Reference;
        h.Append("<section class=\"kpis\">");
        Kpi(h, "Average score", F0(p.AverageTotal), $"best {I(p.Games.Max(g => g.Game.Total))} · worst {I(p.Games.Min(g => g.Game.Total))}");
        Kpi(h, "Luck-free score", F0(p.AverageLuckFree), $"reference {F0(reference.LuckFreeMean)}");
        Kpi(h, "Dice luck per game", Signed(p.Games.Average(g => g.Luck)), "0 is average luck");
        Kpi(h, "Decision cost per game", F1(p.AverageCost), "points given up vs reference");
        Kpi(h, "Matches reference", Pct(p.MatchRate), $"{I(p.Decisions.Count())} decisions");
        h.Append("</section>");

        Breakdown(h, result, p);
        Findings(h, p);
        Mistakes(h, p);
        Decisions(h, p.Profile);
        Boxes(h, p.Profile, reference);
        Upper(h, p, reference);
        FillOrder(h, p.Profile);
        Keeps(h, p.Profile);
        Style(h, p);
    }

    private static void RulesTable(StringBuilder h)
    {
        h.Append("<section class=\"panel\"><h2>Rules</h2><p class=\"muted\">Every finding comes from one of these fixed rules. A rule appears in the findings only when its threshold is met.</p>");
        h.Append("<div class=\"scroll\"><table><thead><tr><th>Rule</th><th>Condition</th><th>Threshold</th></tr></thead><tbody>");
        foreach (var rule in StrategyRules.All)
        {
            h.Append("<tr><td><strong>").Append(E(rule.Title)).Append("</strong><br><code>").Append(E(rule.Id)).Append("</code></td><td>")
                .Append(E(rule.Condition)).Append("</td><td>").Append(E(rule.Threshold)).Append("</td></tr>");
        }

        h.Append("</tbody></table></div></section>");
    }

    private static void Breakdown(StringBuilder h, AnalysisResult result, PlayerAnalysis p)
    {
        h.Append("<section class=\"panel\"><h2>Where the points came from</h2>");
        h.Append("<p class=\"muted\">Each total splits exactly into four parts. <strong>Baseline</strong>: six cards played one at a time with perfect play (")
            .Append(I(6)).Append(" × ").Append(F1(result.CardStartValue)).Append("). <strong>Full Sheet choice</strong>: the extra value of picking among every open box on six cards each turn. ")
            .Append("<strong>Dice luck</strong>: how much better or worse the dice fell than expected (averages zero over many games). ")
            .Append("<strong>Decision cost</strong>: expected points given up where choices differed from the reference strategy.</p>");
        h.Append("<div class=\"falls\">");
        foreach (var g in p.Games)
        {
            Waterfall(h, g);
        }

        h.Append("</div><div class=\"scroll\"><table><thead><tr><th>Game</th><th class=\"num\">Total</th><th class=\"num\">Full Sheet choice</th><th class=\"num\">Dice luck</th><th class=\"num\">Decision cost</th><th class=\"num\">Luck-free</th><th class=\"num\" title=\"Share of simulated reference games with a lower total\">Beats ref. total</th><th class=\"num\" title=\"Share of simulated reference games with a lower luck-free score\">Beats ref. luck-free</th><th class=\"num\">Upper bonuses</th></tr></thead><tbody>");
        foreach (var g in p.Games)
        {
            double luckFree = g.Game.Total - g.Luck;
            h.Append("<tr><td class=\"nowrap\">").Append(Date(g.Game.PlayedAt)).Append(" <code>").Append(E(Short(g.Game.GameId))).Append("</code></td>")
                .Append(Num(I(g.Game.Total))).Append(Num(Signed(g.FullSheetChoice)))
                .Append(Num(Signed(g.Luck), g.Luck >= 0 ? "pos" : "neg")).Append(Num("−" + F1(g.Cost), "neg"))
                .Append(Num(F0(luckFree)))
                .Append(Num(Pct(SimulationSummary.PercentileOf(result.Reference.SortedTotals, g.Game.Total))))
                .Append(Num(Pct(SimulationSummary.PercentileOf(result.Reference.SortedLuckFree, luckFree))))
                .Append(Num(I(Enumerable.Range(0, YahtzeeGameEngine.GameCount).Count(g.Game.HasUpperBonus)) + " / 6"))
                .Append("</tr>");
        }

        h.Append("</tbody></table></div><p class=\"muted small\">Beats ref.: share of the simulated reference-strategy games that this game beat, by total and by luck-free score.</p></section>");
    }

    private static void Waterfall(StringBuilder h, GradedGame g)
    {
        var steps = new (string Label, double From, double To, string Css)[]
        {
            ("Baseline", 0, g.Baseline, "base"),
            ("Full Sheet choice", g.Baseline, g.Baseline + g.FullSheetChoice, "gain"),
            ("Dice luck", g.Baseline + g.FullSheetChoice, g.Baseline + g.FullSheetChoice + g.Luck, g.Luck >= 0 ? "luck" : "loss"),
            ("Decision cost", g.Baseline + g.FullSheetChoice + g.Luck, g.Game.Total, "loss"),
            ("Total", 0, g.Game.Total, "total")
        };
        double[] running = [g.Baseline, g.Baseline + g.FullSheetChoice, g.Baseline + g.FullSheetChoice + g.Luck, g.Game.Total];
        double lo = Math.Max(0, Math.Floor((running.Min() - 100) / 100) * 100);
        double hi = Math.Ceiling((running.Max() + 60) / 100) * 100;
        const double W = 520, H = 210, Left = 44, Bottom = 40, Top = 22;
        double barW = (W - Left - 10) / steps.Length;
        double Y(double v) => Top + (H - Top - Bottom) * (1 - (Math.Max(v, lo) - lo) / (hi - lo));

        h.Append("<figure class=\"fall\"><figcaption>").Append(Date(g.Game.PlayedAt)).Append(" · ").Append(I(g.Game.Total))
            .Append(" <span class=\"muted small\">(axis starts at ").Append(F0(lo)).Append(")</span></figcaption>");
        h.Append("<svg viewBox=\"0 0 ").Append(F0(W)).Append(' ').Append(F0(H)).Append("\" role=\"img\" aria-label=\"Score breakdown\">");
        for (double t = lo; t <= hi + 0.1; t += (hi - lo) / 4)
        {
            h.Append("<line x1=\"").Append(F0(Left)).Append("\" x2=\"").Append(F0(W - 6)).Append("\" y1=\"").Append(F1(Y(t))).Append("\" y2=\"").Append(F1(Y(t)))
                .Append("\" class=\"grid\"/><text x=\"").Append(F0(Left - 6)).Append("\" y=\"").Append(F1(Y(t) + 4)).Append("\" class=\"axis\" text-anchor=\"end\">").Append(F0(t)).Append("</text>");
        }

        for (int i = 0; i < steps.Length; i++)
        {
            var s = steps[i];
            double x = Left + i * barW + 8;
            double y1 = Y(Math.Max(s.From, s.To));
            double y2 = Y(Math.Min(s.From, s.To));
            double value = s.Label is "Baseline" or "Total" ? s.To : s.To - s.From;
            h.Append("<rect x=\"").Append(F1(x)).Append("\" y=\"").Append(F1(y1)).Append("\" width=\"").Append(F1(barW - 16)).Append("\" height=\"")
                .Append(F1(Math.Max(1.5, y2 - y1))).Append("\" rx=\"4\" class=\"").Append(s.Css).Append("\"/>");
            h.Append("<text x=\"").Append(F1(x + (barW - 16) / 2)).Append("\" y=\"").Append(F1(y1 - 6)).Append("\" class=\"val\" text-anchor=\"middle\">")
                .Append(s.Label is "Baseline" or "Total" ? F0(value) : Signed(value)).Append("</text>");
            h.Append("<text x=\"").Append(F1(x + (barW - 16) / 2)).Append("\" y=\"").Append(F0(H - Bottom + 16)).Append("\" class=\"lbl\" text-anchor=\"middle\">")
                .Append(E(s.Label)).Append("</text>");
        }

        h.Append("</svg></figure>");
    }

    private static void Findings(StringBuilder h, PlayerAnalysis p)
    {
        var fired = p.Findings.Where(f => f.Fired).ToList();
        var quiet = p.Findings.Where(f => !f.Fired).ToList();
        h.Append("<section class=\"panel\"><h2>Findings</h2>");
        if (fired.Count == 0)
        {
            h.Append("<p class=\"muted\">No rule reached its threshold.</p>");
        }

        foreach (var f in fired)
        {
            h.Append("<article class=\"finding\"><header><h3>").Append(E(f.Rule.Title)).Append("</h3><code>").Append(E(f.Rule.Id)).Append("</code></header>");
            h.Append("<p>").Append(E(f.Sentence)).Append("</p><div class=\"chips\">");
            h.Append("<span class=\"chip\">").Append(I(f.Match.Cases)).Append(" of ").Append(I(f.Match.Opportunities)).Append("</span>");
            if (f.Match.Opportunities > 0)
            {
                h.Append("<span class=\"chip\">").Append(Pct(f.Match.Rate)).Append("</span>");
            }

            if (f.Match.PointsLost > 0)
            {
                h.Append("<span class=\"chip warn\">").Append(F1(f.Match.PointsLost)).Append(" points</span>");
            }

            h.Append("</div>");
            if (f.Match.Examples.Count > 0)
            {
                h.Append("<table class=\"examples\"><tbody>");
                foreach (var d in f.Match.Examples)
                {
                    DecisionRow(h, d);
                }

                h.Append("</tbody></table>");
            }

            h.Append("<p class=\"rule-def\"><strong>Rule:</strong> ").Append(E(f.Rule.Condition)).Append(' ').Append(E(f.Rule.Threshold)).Append("</p></article>");
        }

        if (quiet.Count > 0)
        {
            h.Append("<details><summary>").Append(I(quiet.Count)).Append(" rules checked and not triggered</summary><table><thead><tr><th>Rule</th><th class=\"num\">Cases</th><th>Threshold</th></tr></thead><tbody>");
            foreach (var f in quiet)
            {
                h.Append("<tr><td>").Append(E(f.Rule.Title)).Append(" <code>").Append(E(f.Rule.Id)).Append("</code></td>")
                    .Append(Num(I(f.Match.Cases) + " of " + I(f.Match.Opportunities))).Append("<td>").Append(E(f.Rule.Threshold)).Append("</td></tr>");
            }

            h.Append("</tbody></table></details>");
        }

        h.Append("</section>");
    }

    private static void Mistakes(StringBuilder h, PlayerAnalysis p)
    {
        var top = p.Decisions
            .Where(d => !d.MatchesReference)
            .OrderByDescending(d => d.Cost)
            .ThenBy(d => d.GameId, StringComparer.Ordinal)
            .ThenBy(d => d.Turn)
            .ThenBy(d => d.Kind)
            .Take(12)
            .ToList();
        h.Append("<section class=\"panel\"><h2>Biggest differences from the reference</h2>");
        if (top.Count == 0)
        {
            h.Append("<p class=\"muted\">Every decision matched the reference strategy.</p></section>");
            return;
        }

        h.Append("<table class=\"examples\"><thead><tr><th>When</th><th>Dice</th><th>Decision</th><th>You</th><th>Reference</th><th class=\"num\">Cost</th></tr></thead><tbody>");
        foreach (var d in top)
        {
            DecisionRow(h, d);
        }

        h.Append("</tbody></table></section>");
    }

    private static void DecisionRow(StringBuilder h, GradedDecision d)
    {
        h.Append("<tr><td class=\"when\">").Append(GameTag(d)).Append("Turn ").Append(I(d.Turn)).Append(", roll ").Append(I(d.RollNumber)).Append("</td><td>")
            .Append(Dice(d.Dice)).Append("</td><td class=\"kind\">").Append(E(Kind(d.Kind))).Append("</td><td class=\"you\"><span class=\"lab\">You: </span>")
            .Append(E(d.Chosen)).Append("</td><td class=\"ref\"><span class=\"lab\">Reference: </span><strong>")
            .Append(E(d.Best)).Append("</strong></td>").Append(Num(F1(d.Cost), "neg")).Append("</tr>");

        static string GameTag(GradedDecision d) => "<code>" + E(Short(d.GameId)) + "</code> ";
    }

    private static void Decisions(StringBuilder h, StrategyProfile profile)
    {
        double max = Math.Max(1, profile.Decisions.Max(d => d.TotalCost));
        h.Append("<section class=\"panel\"><h2>Decision quality</h2><table><thead><tr><th>Decision</th><th class=\"num\">Count</th><th class=\"num\">Matches reference</th><th class=\"num\">Average cost</th><th>Total cost</th><th class=\"num\">Median time</th></tr></thead><tbody>");
        foreach (var d in profile.Decisions.Where(d => d.Count > 0))
        {
            h.Append("<tr><td>").Append(E(Kind(d.Kind))).Append("</td>").Append(Num(I(d.Count))).Append(Num(Pct(d.MatchRate))).Append(Num(F1(d.AverageCost)))
                .Append("<td>").Append(Bar(d.TotalCost / max, F1(d.TotalCost), "neg")).Append("</td>").Append(Num(Seconds(d.MedianSeconds))).Append("</tr>");
        }

        h.Append("</tbody></table><p class=\"muted small\">Cost per game is the total cost divided by the number of games. A keep that matches the reference costs 0.</p></section>");
    }

    private static void Boxes(StringBuilder h, StrategyProfile profile, SimulationSummary reference)
    {
        h.Append("<section class=\"panel\"><h2>Box by box</h2><table><thead><tr><th>Box</th><th>Average points (you / reference)</th><th class=\"num\">Scratched</th><th class=\"num\">Average turn filled</th></tr></thead><tbody>");
        foreach (var c in profile.Categories)
        {
            double scale = Math.Max(1, Math.Max(c.AveragePoints, c.AverageReferencePoints));
            h.Append("<tr><td>").Append(E(ScoreRules.GetDisplayName(c.Category))).Append("</td><td><div class=\"pair\">")
                .Append(Bar(c.AveragePoints / scale, F1(c.AveragePoints), "you"))
                .Append(Bar(c.AverageReferencePoints / scale, F1(c.AverageReferencePoints), "ref")).Append("</div></td>")
                .Append(Num(I(c.Scratches) + " of " + I(c.Fills))).Append(Num(F1(c.AverageTurn))).Append("</tr>");
        }

        h.Append("</tbody></table><p class=\"legend\"><span class=\"key you\"></span>You <span class=\"key ref\"></span>Reference strategy (")
            .Append(I(reference.Games)).Append(" simulated games)</p></section>");
    }

    private static void Upper(StringBuilder h, PlayerAnalysis p, SimulationSummary reference)
    {
        var profile = p.Profile;
        h.Append("<section class=\"panel\"><h2>Upper section</h2><div class=\"kpis inner\">");
        Kpi(h, "Upper bonus", I(profile.UpperBonuses) + " / " + I(profile.Cards), Pct(profile.UpperBonusRate) + " · reference " + Pct(reference.UpperBonusRate));
        Kpi(h, "Average upper subtotal", F1(profile.AverageUpperSubtotal), "63 earns the bonus");
        h.Append("</div><table><thead><tr><th>Box</th><th>Average count of the number (par is 3)</th></tr></thead><tbody>");
        foreach (var c in profile.Categories.Where(c => c.Category < ScoreCategory.ThreeOfAKind))
        {
            h.Append("<tr><td>").Append(E(ScoreRules.GetDisplayName(c.Category))).Append("</td><td>")
                .Append(Bar(c.AverageCount / 5, F1(c.AverageCount), c.AverageCount >= 3 ? "you" : "neg", parAt: 3.0 / 5)).Append("</td></tr>");
        }

        h.Append("</tbody></table></section>");
    }

    private static void FillOrder(StringBuilder h, StrategyProfile profile)
    {
        int max = 1;
        foreach (int v in profile.FillsByPhase)
        {
            max = Math.Max(max, v);
        }

        h.Append("<section class=\"panel\"><h2>When boxes get filled</h2><p class=\"muted\">Boxes filled in each stretch of 13 turns, across all games. Darker means more.</p>");
        h.Append("<table class=\"heat\"><thead><tr><th>Box</th>");
        for (int phase = 0; phase < StrategyProfile.Phases; phase++)
        {
            int from = phase * StrategyProfile.TurnsPerPhase + 1;
            h.Append("<th class=\"num\">").Append(I(from)).Append('–').Append(I(from + StrategyProfile.TurnsPerPhase - 1)).Append("</th>");
        }

        h.Append("</tr></thead><tbody>");
        foreach (var category in ScoreRules.ScoringCategories)
        {
            h.Append("<tr><td>").Append(E(ScoreRules.GetDisplayName(category))).Append("</td>");
            for (int phase = 0; phase < StrategyProfile.Phases; phase++)
            {
                int v = profile.FillsByPhase[phase, (int)category];
                double a = (double)v / max;
                h.Append("<td class=\"num\" style=\"background:rgba(49,150,106,").Append((0.08 + 0.8 * a).ToString("0.00", Inv)).Append(");")
                    .Append(a > 0.55 ? "color:#fff;" : "").Append("\">").Append(v == 0 ? "" : I(v)).Append("</td>");
            }

            h.Append("</tr>");
        }

        h.Append("</tbody></table></section>");
    }

    private static void Keeps(StringBuilder h, StrategyProfile profile)
    {
        h.Append("<section class=\"panel\"><h2>What you keep</h2><p class=\"muted\">How often each kind of keep was chosen, next to what the reference strategy would keep from the same dice.</p><div class=\"two\">");
        Table("After roll 1", profile.KeepsAfterRoll1);
        Table("After roll 2", profile.KeepsAfterRoll2);
        h.Append("</div><p class=\"muted small\">Straight draw: three or more different numbers that fit one large straight. 5 and 6: one five and one six. Mixed: anything else.</p></section>");

        void Table(string title, IReadOnlyList<PatternCount> rows)
        {
            int max = Math.Max(1, rows.Count == 0 ? 1 : rows.Max(r => Math.Max(r.Player, r.Reference)));
            h.Append("<div><h3>").Append(E(title)).Append("</h3><table><thead><tr><th>Keep</th><th>You / reference</th></tr></thead><tbody>");
            foreach (var r in rows)
            {
                h.Append("<tr><td>").Append(E(KeepPatterns.DisplayName(r.Pattern))).Append("</td><td><div class=\"pair\">")
                    .Append(Bar((double)r.Player / max, I(r.Player), "you")).Append(Bar((double)r.Reference / max, I(r.Reference), "ref")).Append("</div></td></tr>");
            }

            h.Append("</tbody></table></div>");
        }
    }

    private static void Style(StringBuilder h, PlayerAnalysis p)
    {
        var s = p.Profile;
        int turns = s.TurnsByRollsUsed.Sum();
        h.Append("<section class=\"panel\"><h2>Play style</h2><div class=\"kpis inner\">");
        Kpi(h, "Cards in progress", F1(s.AverageCardsInProgress), "started, not finished, on average");
        Kpi(h, "Card switches", Pct(s.CardSwitchRate), "turns scored on a different card than the turn before");
        Kpi(h, "All three rolls used", Pct(Share(3)), $"1 roll {Pct(Share(1))} · 2 rolls {Pct(Share(2))}");
        Kpi(h, "Yahtzees scored", I(s.YahtzeesScored), F1((double)s.YahtzeesScored / Math.Max(1, s.Games)) + " per game");
        Kpi(h, "Median turn time", Seconds(s.MedianTurnSeconds), "first roll to score");
        Kpi(h, "Changes of mind", I(s.ChangesOfMind), "same die pressed twice before a roll");
        Kpi(h, "Undo", I(s.Undos), $"{I(s.UndosChangedBox)} changed the box · {I(s.UndosAfterNextRoll)} after the next roll");
        h.Append("</div></section>");

        double Share(int rolls) => turns == 0 ? 0 : (double)s.TurnsByRollsUsed[rolls] / turns;
    }

    private static void Method(StringBuilder h, AnalysisResult result)
    {
        var r = result.Reference;
        var g = result.Greedy;
        h.Append("<section class=\"panel\"><h2>How this report works</h2><ol class=\"method\">");
        h.Append("<li><strong>Replay.</strong> Every finished Full Sheet game is rebuilt from its recorded rolls, holds, scores and undos. A game is used only if the replay matches the submitted total exactly.</li>");
        h.Append("<li><strong>Reference strategy.</strong> An exact solver finds the best play for a single card (expected ")
            .Append(F1(result.CardStartValue)).Append(" points per card with these rules). On the Full Sheet each roll may go in any open box on any of the six cards; the reference strategy picks the box that adds the most points now plus expected future points for that card, and keeps the dice that maximise that value.</li>");
        h.Append("<li><strong>Grading.</strong> For every keep, early stop and box choice, the cost is the expected points of the reference choice minus the expected points of the player's choice. Matching the reference costs 0.</li>");
        h.Append("<li><strong>Luck.</strong> After each roll, luck is the value of the dice that came up minus the value expected before rolling. Luck averages zero, so the <em>luck-free score</em> (total minus luck) compares skill without the dice.</li>");
        h.Append("<li><strong>Rules.</strong> Findings come only from the fixed rule table below. Each states its condition, threshold and evidence. No other judgement is added.</li></ol>");
        h.Append("<div class=\"scroll\"><table><thead><tr><th>Simulated strategy (").Append(I(r.Games)).Append(" games each, seeded)</th><th class=\"num\">Average</th><th class=\"num\">Spread (SD)</th><th class=\"num\">10th–90th percentile</th><th class=\"num\">Luck-free average</th><th class=\"num\">Upper bonus</th></tr></thead><tbody>");
        foreach (var s in new[] { r, g })
        {
            h.Append("<tr><td>").Append(E(s.Strategy)).Append("</td>").Append(Num(F1(s.Mean))).Append(Num(F1(s.StandardDeviation)))
                .Append(Num(F0(SimulationSummary.Quantile(s.SortedTotals, 0.1)) + "–" + F0(SimulationSummary.Quantile(s.SortedTotals, 0.9))))
                .Append(Num(s == r ? F1(s.LuckFreeMean) : "—")).Append(Num(Pct(s.UpperBonusRate))).Append("</tr>");
        }

        h.Append("</tbody></table><p class=\"muted small\">Greedy always takes the most points now and keeps dice for that. Seed ")
            .Append(I(result.ReferenceData.Seed)).Append(". The same games and seed always give the same report.</p></div></section>");
    }

    private static string Dice(string dice)
    {
        var sb = new StringBuilder("<span class=\"dice\">");
        foreach (char ch in dice)
        {
            sb.Append(Die(ch - '0'));
        }

        return sb.Append("</span>").ToString();
    }

    private static string Die(int value)
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
        var sb = new StringBuilder("<svg class=\"die\" viewBox=\"0 0 24 24\" aria-label=\"").Append(I(value)).Append("\"><rect x=\"1\" y=\"1\" width=\"22\" height=\"22\" rx=\"5\"/>");
        foreach (var p in pips)
        {
            sb.Append("<circle cx=\"").Append(I(p[0] * 6)).Append("\" cy=\"").Append(I(p[1] * 6)).Append("\" r=\"2.3\"/>");
        }

        return sb.Append("</svg>").ToString();
    }

    private static string Bar(double fraction, string label, string css, double? parAt = null)
    {
        double pct = Math.Clamp(fraction, 0, 1) * 100;
        var sb = new StringBuilder("<div class=\"bar ").Append(css).Append("\"><span style=\"width:").Append(F1(pct)).Append("%\"></span>");
        if (parAt is { } par)
        {
            sb.Append("<i style=\"left:").Append(F1(par * 100)).Append("%\"></i>");
        }

        return sb.Append("<b>").Append(E(label)).Append("</b></div>").ToString();
    }

    private static void Kpi(StringBuilder h, string label, string value, string note) =>
        h.Append("<div class=\"kpi\"><span>").Append(E(label)).Append("</span><strong>").Append(E(value)).Append("</strong><small>").Append(E(note)).Append("</small></div>");

    private static void Mini(StringBuilder h, string label, string value) =>
        h.Append("<div><dt>").Append(E(label)).Append("</dt><dd>").Append(E(value)).Append("</dd></div>");

    private static string Num(string text, string? css = null) =>
        "<td class=\"num" + (css == null ? "" : " " + css) + "\">" + E(text) + "</td>";

    private static string Short(string id) => id.Length <= 8 ? id : id[..8];

    private static string E(string text) => WebUtility.HtmlEncode(text);

    private static void Start(StringBuilder h, string title)
    {
        h.Append("<!doctype html><html lang=\"en\"><head><meta charset=\"utf-8\"><meta name=\"viewport\" content=\"width=device-width, initial-scale=1\">");
        h.Append("<meta name=\"robots\" content=\"noindex\"><title>").Append(E(title)).Append("</title><style>body{margin:0;background:#f1efe6}")
            .Append(Css).Append("</style></head><body><div class=\"sr\">");
    }

    private static void End(StringBuilder h, AnalysisResult result)
    {
        h.Append("</main><footer class=\"wrap\">Generated by scripts/analyze-strategy.ps1 from exports/ · rules v").Append(E(result.RulesVersion))
            .Append(" · seed ").Append(I(result.ReferenceData.Seed)).Append(" · ").Append(I(result.ReferenceData.Simulations))
            .Append(" simulated games per strategy · personal gameplay data, do not share or commit.</footer></div></body></html>\n");
    }

    // Every rule is scoped under .sr so the same markup can be embedded in the app without touching its styles.
    private const string Css = """
.sr{--paper:#f8f6ee;--shell:#f1efe6;--line:#dedfd5;--ink:#1f2933;--forest:#17352c;--forest-dark:#102a25;--muted:#56645c;--accent:#1f6f5f;--mint:#b8efc7;--mint-deep:#31966a;--gold:#f4c66a;--bad:#b4483c;--die:#fbf8ee;color:var(--ink);font:15px/1.5 "Trebuchet MS","Segoe UI",sans-serif;-webkit-text-size-adjust:100%;text-align:left}
.sr *{box-sizing:border-box}
.sr .wrap{max-width:1100px;margin:0 auto;padding:0 20px}
.sr .hero{background:linear-gradient(135deg,var(--forest-dark),var(--forest) 60%,var(--accent));color:#fff;padding:34px 0 30px;margin-bottom:24px}
.sr .hero h1{font-family:Georgia,"Times New Roman",serif;font-size:2.3rem;margin:4px 0 6px;color:var(--gold)}
.sr .eyebrow{margin:0;text-transform:uppercase;letter-spacing:.12em;font-size:.75rem;color:var(--mint)}
.sr .eyebrow a{color:var(--mint);text-decoration:none}.sr .eyebrow a:hover{text-decoration:underline}
.sr .lede{margin:0;max-width:760px;color:#e7efe9}
.sr .intro{margin:0 0 14px;color:var(--muted)}
.sr h2{font-family:Georgia,"Times New Roman",serif;color:var(--forest);margin:0 0 10px;font-size:1.35rem}
.sr h3{margin:0 0 6px;font-size:1.02rem;color:var(--forest)}
.sr section{margin-bottom:22px}
.sr .panel{background:var(--paper);border:1px solid var(--line);border-radius:14px;padding:20px 22px;box-shadow:0 1px 2px rgba(16,42,37,.06);overflow-x:auto}
.sr .muted{color:var(--muted)}.sr .small{font-size:.85rem}
.sr .kpis{display:grid;grid-template-columns:repeat(auto-fit,minmax(170px,1fr));gap:12px}
.sr .kpis.inner{margin-bottom:14px}
.sr .kpi{background:var(--paper);border:1px solid var(--line);border-radius:12px;padding:12px 14px;display:flex;flex-direction:column;gap:2px}
.sr .kpi span{font-size:.78rem;text-transform:uppercase;letter-spacing:.06em;color:var(--muted)}
.sr .kpi strong{font-size:1.55rem;color:var(--forest);font-family:Georgia,serif}
.sr .kpi small{color:var(--muted);font-size:.8rem}
.sr .players{display:grid;grid-template-columns:repeat(auto-fit,minmax(250px,1fr));gap:14px}
.sr .player-card{display:flex;flex-direction:column;gap:6px;background:var(--paper);border:1px solid var(--line);border-radius:14px;padding:18px;color:inherit;text-decoration:none;transition:transform .12s,box-shadow .12s}
.sr .player-card:hover{transform:translateY(-2px);box-shadow:0 6px 18px rgba(16,42,37,.12)}
.sr .player-card h3{font-family:Georgia,serif;font-size:1.3rem;margin:0}
.sr .player-card .go{margin-top:auto;color:var(--accent);font-weight:bold}
.sr .mini{display:grid;grid-template-columns:1fr 1fr;gap:6px 12px;margin:4px 0}
.sr .mini dt{font-size:.72rem;text-transform:uppercase;letter-spacing:.05em;color:var(--muted)}
.sr .mini dd{margin:0;font-size:1.15rem;font-weight:bold;color:var(--forest)}
.sr .chips{display:flex;flex-wrap:wrap;gap:6px;margin:6px 0}
.sr .chip{background:var(--mint);color:var(--forest-dark);border-radius:999px;padding:2px 10px;font-size:.8rem}
.sr .chip.warn{background:#f8dcd6;color:#7a2a20}
.sr table{width:100%;border-collapse:collapse;margin:8px 0;font-size:.92rem}
.sr th{text-align:left;font-size:.75rem;text-transform:uppercase;letter-spacing:.05em;color:var(--muted);border-bottom:2px solid var(--line);padding:6px 8px}
.sr td{border-bottom:1px solid var(--line);padding:6px 8px;vertical-align:middle}
.sr tbody tr:nth-child(even) td{background:rgba(241,239,230,.6)}
.sr .num{text-align:right;font-variant-numeric:tabular-nums;white-space:nowrap}
.sr .pos{color:var(--mint-deep);font-weight:bold}.sr .neg{color:var(--bad)}
.sr code{font-size:.8em;background:rgba(23,53,44,.07);padding:1px 5px;border-radius:4px;color:var(--muted)}
.sr .finding{border-left:4px solid var(--gold);background:#fff;border-radius:10px;padding:12px 16px;margin:12px 0}
.sr .finding header{display:flex;justify-content:space-between;align-items:baseline;gap:10px}
.sr .finding p{margin:4px 0}
.sr .rule-def{font-size:.82rem;color:var(--muted)}
.sr details{margin-top:10px}.sr summary{cursor:pointer;color:var(--accent);font-weight:bold}
.sr .dice{display:inline-flex;gap:3px}
.sr .die{width:22px;height:22px}.sr .die rect{fill:var(--die);stroke:var(--forest);stroke-width:1.4}.sr .die circle{fill:var(--forest)}
.sr .when{white-space:nowrap;color:var(--muted);font-size:.85rem}
.sr .lab{display:none}
.sr .bar{position:relative;height:20px;background:rgba(23,53,44,.06);border-radius:5px;min-width:120px;overflow:hidden}
.sr .bar span{position:absolute;inset:0 auto 0 0;border-radius:5px}
.sr .bar b{position:relative;font-size:.78rem;padding-left:6px;line-height:20px;color:var(--ink)}
.sr .bar i{position:absolute;top:0;bottom:0;width:2px;background:var(--forest)}
.sr .bar.you span{background:var(--mint-deep);opacity:.75}.sr .bar.ref span{background:var(--gold)}.sr .bar.neg span{background:#e7a69c}
.sr .pair{display:flex;flex-direction:column;gap:3px}
.sr .legend{font-size:.82rem;color:var(--muted)}.sr .key{display:inline-block;width:12px;height:12px;border-radius:3px;margin:0 4px 0 12px;vertical-align:-2px}
.sr .key.you{background:var(--mint-deep);opacity:.75}.sr .key.ref{background:var(--gold)}
.sr .heat td.num,.sr .heat th.num{text-align:center;min-width:52px}
.sr .two{display:grid;grid-template-columns:repeat(auto-fit,minmax(min(300px,100%),1fr));gap:18px}
.sr .falls{display:grid;grid-template-columns:repeat(auto-fit,minmax(min(320px,100%),1fr));gap:14px;margin:10px 0}
.sr .fall{margin:0;background:#fff;border:1px solid var(--line);border-radius:10px;padding:8px;max-width:560px}
.sr .fall figcaption{font-weight:bold;color:var(--forest);padding:2px 6px}
.sr .fall svg{width:100%;height:auto}
.sr .fall .grid{stroke:var(--line);stroke-width:1}.sr .fall .axis{font-size:10px;fill:var(--muted)}
.sr .fall .val{font-size:11px;font-weight:bold;fill:var(--ink)}.sr .fall .lbl{font-size:10.5px;fill:var(--muted)}
.sr .fall .base{fill:#9fb3aa}.sr .fall .gain{fill:var(--mint-deep)}.sr .fall .luck{fill:var(--gold)}.sr .fall .loss{fill:var(--bad)}.sr .fall .total{fill:var(--forest)}
.sr .method li{margin-bottom:6px}
.sr .scroll{overflow-x:auto}.sr .nowrap{white-space:nowrap}
.sr footer{color:var(--muted);font-size:.8rem;padding:10px 20px 40px}
.sr-embedded footer{padding:4px 0 0}
@media (max-width:640px){.sr .hero h1{font-size:1.8rem}.sr .panel{padding:14px}.sr td,.sr th{padding:5px}.sr .bar{min-width:80px}.sr .kpis{grid-template-columns:repeat(auto-fit,minmax(140px,1fr))}
.sr table.examples,.sr table.examples tbody{display:block}.sr table.examples thead{display:none}.sr table.examples tr{display:flex;flex-wrap:wrap;align-items:center;gap:4px 10px;padding:8px 0;border-bottom:1px solid var(--line)}
.sr table.examples td{border:0;padding:0;background:none!important}.sr table.examples td.you,.sr table.examples td.ref{flex-basis:100%}
.sr table.examples td.kind{color:var(--muted);font-size:.85rem}.sr table.examples td.num{margin-left:auto}.sr .lab{display:inline;color:var(--muted)}}
@media print{.sr .hero{background:var(--forest);-webkit-print-color-adjust:exact;print-color-adjust:exact}.sr .panel,.sr .kpi{box-shadow:none}}
""";
}

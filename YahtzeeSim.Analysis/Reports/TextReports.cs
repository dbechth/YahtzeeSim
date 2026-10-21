using System.Text;
using YahtzeeSim.Analysis.Data;
using YahtzeeSim.Analysis.Grading;
using YahtzeeSim.Analysis.Solver;
using YahtzeeSim.Engine;
using static YahtzeeSim.Analysis.Reports.Fmt;

namespace YahtzeeSim.Analysis.Reports;

/// <summary>Plain-text versions of the report: one Markdown file per player and a CSV of every graded decision.</summary>
public static class TextReports
{
    public static string Markdown(AnalysisResult result, PlayerAnalysis p)
    {
        var m = new StringBuilder();
        var reference = result.Reference;
        m.Append("# ").Append(p.Nickname).Append(" · Full Sheet strategy\n\n");
        m.Append(I(p.Games.Count)).Append(" finished game(s). Rules v").Append(result.RulesVersion).Append(". Reference strategy average ")
            .Append(F1(reference.Mean)).Append(" (luck-free ").Append(F1(reference.LuckFreeMean)).Append(") over ").Append(I(reference.Games)).Append(" simulated games.\n\n");

        m.Append("## Summary\n\n| Measure | Value |\n|---|---|\n");
        m.Append("| Average score | ").Append(F1(p.AverageTotal)).Append(" |\n");
        m.Append("| Luck-free score | ").Append(F1(p.AverageLuckFree)).Append(" |\n");
        m.Append("| Dice luck per game | ").Append(Signed(p.Games.Average(g => g.Luck))).Append(" |\n");
        m.Append("| Decision cost per game | ").Append(F1(p.AverageCost)).Append(" |\n");
        m.Append("| Decisions matching the reference | ").Append(Pct(p.MatchRate)).Append(" |\n");
        m.Append("| Upper bonus | ").Append(I(p.Profile.UpperBonuses)).Append(" / ").Append(I(p.Profile.Cards)).Append(" |\n\n");

        m.Append("## Games\n\n| Date | Game | Total | Baseline | Full Sheet choice | Dice luck | Decision cost |\n|---|---|---:|---:|---:|---:|---:|\n");
        foreach (var g in p.Games)
        {
            m.Append("| ").Append(Date(g.Game.PlayedAt)).Append(" | `").Append(g.Game.GameId[..Math.Min(8, g.Game.GameId.Length)]).Append("` | ")
                .Append(I(g.Game.Total)).Append(" | ").Append(F1(g.Baseline)).Append(" | ").Append(Signed(g.FullSheetChoice)).Append(" | ")
                .Append(Signed(g.Luck)).Append(" | −").Append(F1(g.Cost)).Append(" |\n");
        }

        m.Append("\n## Findings\n\n");
        var fired = p.Findings.Where(f => f.Fired).ToList();
        if (fired.Count == 0)
        {
            m.Append("No rule reached its threshold.\n");
        }

        foreach (var f in fired)
        {
            m.Append("- **").Append(f.Rule.Title).Append("** (`").Append(f.Rule.Id).Append("`): ").Append(f.Sentence).Append('\n');
            foreach (var d in f.Match.Examples)
            {
                m.Append("  - Turn ").Append(I(d.Turn)).Append(", roll ").Append(I(d.RollNumber)).Append(", dice ").Append(d.Dice).Append(": ")
                    .Append(d.Chosen).Append(" → reference ").Append(d.Best).Append(" (cost ").Append(F1(d.Cost)).Append(")\n");
            }
        }

        m.Append("\nRules checked and not triggered: ")
            .Append(string.Join(", ", p.Findings.Where(f => !f.Fired).Select(f => $"`{f.Rule.Id}` ({I(f.Match.Cases)} of {I(f.Match.Opportunities)})")))
            .Append(".\n\n## Decision quality\n\n| Decision | Count | Matches | Average cost | Total cost |\n|---|---:|---:|---:|---:|\n");
        foreach (var d in p.Profile.Decisions.Where(d => d.Count > 0))
        {
            m.Append("| ").Append(Kind(d.Kind)).Append(" | ").Append(I(d.Count)).Append(" | ").Append(Pct(d.MatchRate)).Append(" | ")
                .Append(F1(d.AverageCost)).Append(" | ").Append(F1(d.TotalCost)).Append(" |\n");
        }

        m.Append("\n## Box by box\n\n| Box | Average points | Reference | Scratched | Average turn |\n|---|---:|---:|---:|---:|\n");
        foreach (var c in p.Profile.Categories)
        {
            m.Append("| ").Append(ScoreRules.GetDisplayName(c.Category)).Append(" | ").Append(F1(c.AveragePoints)).Append(" | ")
                .Append(F1(c.AverageReferencePoints)).Append(" | ").Append(I(c.Scratches)).Append(" of ").Append(I(c.Fills)).Append(" | ")
                .Append(F1(c.AverageTurn)).Append(" |\n");
        }

        m.Append("\n## Biggest differences from the reference\n\n| Game | Turn | Roll | Dice | Decision | You | Reference | Cost |\n|---|---:|---:|---|---|---|---|---:|\n");
        foreach (var d in p.Decisions.Where(d => !d.MatchesReference).OrderByDescending(d => d.Cost).ThenBy(d => d.GameId, StringComparer.Ordinal).ThenBy(d => d.Turn).ThenBy(d => d.Kind).Take(12))
        {
            m.Append("| `").Append(d.GameId[..Math.Min(8, d.GameId.Length)]).Append("` | ").Append(I(d.Turn)).Append(" | ").Append(I(d.RollNumber)).Append(" | ")
                .Append(d.Dice).Append(" | ").Append(Kind(d.Kind)).Append(" | ").Append(d.Chosen).Append(" | ").Append(d.Best).Append(" | ")
                .Append(F1(d.Cost)).Append(" |\n");
        }

        return m.ToString();
    }

    public static string DecisionsCsv(AnalysisResult result)
    {
        var sb = new StringBuilder();
        sb.Append("player,game_id,turn,roll_number,decision,dice,chosen,reference,chosen_value,reference_value,cost,matches_reference,chosen_pattern,reference_pattern,think_seconds\n");
        foreach (var p in result.Players)
        {
            foreach (var d in p.Decisions)
            {
                string[] fields =
                [
                    p.Nickname,
                    d.GameId,
                    I(d.Turn),
                    I(d.RollNumber),
                    d.Kind.ToString(),
                    d.Dice,
                    d.Chosen,
                    d.Best,
                    d.ChosenValue.ToString("0.0000", Inv),
                    d.BestValue.ToString("0.0000", Inv),
                    d.Cost.ToString("0.0000", Inv),
                    d.MatchesReference ? "1" : "0",
                    d.ChosenPattern?.ToString() ?? string.Empty,
                    d.BestPattern?.ToString() ?? string.Empty,
                    d.ThinkSeconds.ToString("0.000", Inv)
                ];
                sb.Append(string.Join(',', fields.Select(Csv.Escape))).Append('\n');
            }
        }

        return sb.ToString();
    }
}

/// <summary>Writes every report file for an analysis.</summary>
public static class ReportWriter
{
    public static IReadOnlyList<string> WriteAll(AnalysisResult result, string outputDirectory)
    {
        Directory.CreateDirectory(outputDirectory);
        var written = new List<string>();
        var utf8 = new UTF8Encoding(false);

        Write("index.html", HtmlReport.Index(result));
        foreach (var p in result.Players)
        {
            Write(p.Slug + ".html", HtmlReport.Player(result, p));
            Write(p.Slug + ".md", TextReports.Markdown(result, p));
        }

        Write("decisions.csv", TextReports.DecisionsCsv(result));
        return written;

        void Write(string name, string content)
        {
            string path = Path.Combine(outputDirectory, name);
            File.WriteAllText(path, content, utf8);
            written.Add(path);
        }
    }
}

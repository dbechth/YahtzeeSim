using System.Diagnostics;
using System.IO;
using YahtzeeSim.Analysis;
using YahtzeeSim.Analysis.Data;
using YahtzeeSim.Analysis.Reports;
using YahtzeeSim.Analysis.Solver;
using YahtzeeSim.Analysis.Story;

namespace YahtzeeSim.Research;

/// <summary>One player as the research app shows them: every view is prebuilt HTML.</summary>
public sealed record ResearchPlayer(
    string Nickname,
    string Slug,
    string BadgeName,
    string BadgeId,
    string Summary,
    string ReportHtml,
    string StoryHtml,
    string BadgeHtml);

public sealed record ResearchSnapshot(string OverviewHtml, IReadOnlyList<ResearchPlayer> Players, DateTime BuiltAt, int Skipped);

/// <summary>
/// Pulls data with scripts/export-game-data.ps1 and runs the shared analysis. Uses the solver table and reference
/// simulations shipped with the web app, so the numbers match what players see.
/// </summary>
public sealed class ResearchSession(string repoRoot)
{
    private const int StyleReferenceGames = 100;

    private CardSolver? _solver;
    private ReferenceData? _reference;
    private StyleMetrics? _styleReference;

    public string RepoRoot { get; } = repoRoot;

    public string ExportsDir => Path.Combine(RepoRoot, "exports");

    public string ReportsDir => Path.Combine(ExportsDir, "analysis");

    private string AssetsDir => Path.Combine(RepoRoot, "YahtzeeSim.Web", "wwwroot", "analysis");

    /// <summary>The folder holding YahtzeeSim.sln, searched upwards from the app's folder and the current folder.</summary>
    public static string? FindRepoRoot()
    {
        foreach (string start in new[] { AppContext.BaseDirectory, Environment.CurrentDirectory })
        {
            for (var dir = new DirectoryInfo(start); dir != null; dir = dir.Parent)
            {
                if (File.Exists(Path.Combine(dir.FullName, "YahtzeeSim.sln")))
                {
                    return dir.FullName;
                }
            }
        }

        return null;
    }

    public bool HasExport => File.Exists(Path.Combine(ExportsDir, "scores.csv")) && File.Exists(Path.Combine(ExportsDir, "actions.csv"));

    /// <summary>Runs the export script (Node and a wrangler login are needed) and streams its output.</summary>
    public async Task<int> PullAsync(Action<string> log, CancellationToken cancel)
    {
        var start = new ProcessStartInfo
        {
            FileName = "powershell.exe",
            WorkingDirectory = RepoRoot,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        foreach (string arg in new[] { "-NoProfile", "-ExecutionPolicy", "Bypass", "-File", Path.Combine(RepoRoot, "scripts", "export-game-data.ps1") })
        {
            start.ArgumentList.Add(arg);
        }

        // npx may not be on PATH for apps started from Explorer; add the default Node location when it exists.
        string node = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "nodejs");
        if (Directory.Exists(node))
        {
            start.Environment["PATH"] = node + Path.PathSeparator + start.Environment["PATH"];
        }

        using var process = new Process { StartInfo = start };
        process.OutputDataReceived += (_, e) => { if (e.Data != null) log(e.Data); };
        process.ErrorDataReceived += (_, e) => { if (e.Data != null) log(e.Data); };
        log("> scripts\\export-game-data.ps1");
        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        try
        {
            await process.WaitForExitAsync(cancel);
        }
        catch (OperationCanceledException)
        {
            process.Kill(entireProcessTree: true);
            throw;
        }

        return process.ExitCode;
    }

    /// <summary>Analyses the current export and builds every view. Also refreshes the script's report files.</summary>
    public ResearchSnapshot Analyze(Action<string> log)
    {
        var tables = DiceTables.Shared;
        _solver ??= LoadSolver(tables, log);
        _reference ??= LoadReference(tables, log);
        var evaluator = new SheetEvaluator(_solver, tables);
        if (_styleReference == null)
        {
            log($"Simulating {StyleReferenceGames} reference games for badge calibration");
            _styleReference = StyleReference.Simulate(evaluator, StyleReferenceGames, AnalysisOptions.DefaultSeed);
        }

        var data = ExportData.Load(ExportsDir);
        var result = AnalysisRunner.Run(data, new AnalysisOptions(), log, _solver, _reference);
        ReportWriter.WriteAll(result, ReportsDir);

        var players = result.Players.Select(p =>
        {
            var story = StoryRules.Build(p);
            var metrics = StyleMetrics.From([.. p.Games.Select(g => g.Game)]);
            return new ResearchPlayer(
                p.Nickname,
                p.Slug,
                story.Badge.Name,
                story.Badge.Id,
                $"{p.Games.Count} game{(p.Games.Count == 1 ? "" : "s")} · avg {p.AverageTotal:0} · luck-free {p.AverageLuckFree:0}",
                Page(HtmlReport.PlayerFragment(result, p), "#f1efe6"),
                Page(StoryReport.Fragment(story), "#f1efe6"),
                Page(BadgeReport.PlayerFragment(p.Nickname, metrics, _styleReference), "#ffffff"));
        }).ToList();

        log($"Analysed {result.GameCount} games from {players.Count} players; {result.Skipped.Count} skipped.");
        return new ResearchSnapshot(HtmlReport.Index(result), players, DateTime.Now, result.Skipped.Count);
    }

    private CardSolver LoadSolver(DiceTables tables, Action<string> log)
    {
        string path = Path.Combine(AssetsDir, "card-values.bin");
        if (File.Exists(path))
        {
            try
            {
                using var stream = File.OpenRead(path);
                log("Card values loaded from the app's shipped table");
                return CardSolver.ReadAsset(stream, tables);
            }
            catch (InvalidDataException)
            {
                log("Shipped card values are out of date; computing");
            }
        }

        return CardSolver.LoadOrCompute(tables, Path.Combine(ReportsDir, "cache", "card-values.bin"), log);
    }

    private ReferenceData LoadReference(DiceTables tables, Action<string> log)
    {
        string path = Path.Combine(AssetsDir, "reference.json");
        if (File.Exists(path))
        {
            try
            {
                var data = ReferenceData.FromJson(File.ReadAllText(path), tables);
                log("Reference simulations loaded from the app's shipped file");
                return data;
            }
            catch (InvalidDataException)
            {
                log("Shipped reference data is out of date; simulating");
            }
        }

        var options = new AnalysisOptions();
        return ReferenceData.Simulate(new SheetEvaluator(_solver!, tables), options.Simulations, options.Seed);
    }

    private static string Page(string fragment, string background) =>
        "<!doctype html><html lang=\"en\"><head><meta charset=\"utf-8\"><meta name=\"viewport\" content=\"width=device-width, initial-scale=1\">"
        + $"<style>body{{margin:0;padding:18px;background:{background}}}</style></head><body>{fragment}</body></html>";
}

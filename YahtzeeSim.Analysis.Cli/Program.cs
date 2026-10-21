using System.Globalization;
using YahtzeeSim.Analysis;
using YahtzeeSim.Analysis.Data;
using YahtzeeSim.Analysis.Reports;

CultureInfo.DefaultThreadCurrentCulture = CultureInfo.InvariantCulture;
CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;

string exportsDir = "exports";
string? outputDir = null;
int simulations = AnalysisOptions.DefaultSimulations;
int seed = AnalysisOptions.DefaultSeed;
bool useCache = true;

for (int i = 0; i < args.Length; i++)
{
    switch (args[i])
    {
        case "--exports":
            exportsDir = Next();
            break;
        case "--out":
            outputDir = Next();
            break;
        case "--simulations":
            simulations = int.Parse(Next(), CultureInfo.InvariantCulture);
            break;
        case "--seed":
            seed = int.Parse(Next(), CultureInfo.InvariantCulture);
            break;
        case "--no-cache":
            useCache = false;
            break;
        case "-h":
        case "--help":
            Console.WriteLine("""
                Full Sheet strategy analysis (rule-based, deterministic).

                Usage: YahtzeeSim.Analysis.Cli [--exports <dir>] [--out <dir>] [--simulations <n>] [--seed <n>] [--no-cache]

                  --exports      Folder with users.csv, scores.csv and actions.csv (default: exports)
                  --out          Report folder (default: <exports>/analysis)
                  --simulations  Simulated games per baseline strategy (default: 1000)
                  --seed         Seed for simulated dice (default: 20261020)
                  --no-cache     Recompute the single-card solver instead of using <out>/cache
                """);
            return 0;
        default:
            Console.Error.WriteLine($"Unknown argument '{args[i]}'. Use --help.");
            return 2;
    }

    string Next() => i + 1 < args.Length ? args[++i] : throw new ArgumentException($"{args[i]} needs a value.");
}

if (simulations < 1)
{
    Console.Error.WriteLine("--simulations must be at least 1.");
    return 2;
}

outputDir ??= Path.Combine(exportsDir, "analysis");

try
{
    var data = ExportData.Load(exportsDir);
    var options = new AnalysisOptions
    {
        Simulations = simulations,
        Seed = seed,
        SolverCachePath = useCache ? Path.Combine(outputDir, "cache", "card-values.bin") : null
    };

    var result = AnalysisRunner.Run(data, options, Console.WriteLine);
    var files = ReportWriter.WriteAll(result, outputDir);

    Console.WriteLine();
    foreach (var p in result.Players)
    {
        Console.WriteLine($"{p.Nickname}: {p.Games.Count} game(s), average {p.AverageTotal:0}, luck-free {p.AverageLuckFree:0}, " +
            $"decision cost {p.AverageCost:0.0} per game, {p.MatchRate:0%} of decisions match the reference, " +
            $"{p.Findings.Count(f => f.Fired)} finding(s).");
    }

    foreach (var s in result.Skipped)
    {
        Console.WriteLine($"Skipped {s.Nickname} {s.GameId[..Math.Min(8, s.GameId.Length)]} ({s.Mode}): {s.Reason}");
    }

    Console.WriteLine();
    Console.WriteLine($"Wrote {files.Count} files. Open {Path.GetFullPath(Path.Combine(outputDir, "index.html"))}");
    return 0;
}
catch (Exception ex) when (ex is FileNotFoundException or InvalidDataException or DirectoryNotFoundException)
{
    Console.Error.WriteLine(ex.Message);
    return 1;
}

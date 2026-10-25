using YahtzeeSim.Analysis.Data;
using YahtzeeSim.Analysis.Reports;
using YahtzeeSim.Analysis.Solver;

namespace YahtzeeSim.Analysis.Tests;

public class EndToEndTests
{
    private static string BuildExport()
    {
        string dir = TestData.TempDirectory();
        var alice = TestData.BotGame(PlacementScoring.CardValues, 31, "Alice", "user-a", "game-a1", 1_800_000_000_000);
        var alice2 = TestData.BotGame(PlacementScoring.CardValues, 33, "Alice", "user-a", "game-a2", 1_800_000_100_000);
        var bob = TestData.BotGame(PlacementScoring.Greedy, 32, "Bob, Jr.", "user-b", "game-b1", 1_800_000_050_000);
        var unfinished = TestData.BotGame(PlacementScoring.Greedy, 34, "Bob, Jr.", "user-b", "game-b2").Actions.Take(30);
        var single = new ScoreRow(99, "Bob, Jr.", "user-b", "game-b3", "Single", 180, 1_800_000_060_000);
        TestData.WriteExport(
            dir,
            [alice.Score, bob.Score, alice2.Score, single],
            alice.Actions.Concat(bob.Actions).Concat(alice2.Actions).Concat(unfinished));
        return dir;
    }

    [Fact]
    public void ExportIsAnalysedAndReportsAreWritten()
    {
        string dir = BuildExport();
        var data = ExportData.Load(dir);
        var result = AnalysisRunner.Run(data, new AnalysisOptions { Simulations = 30 }, solver: TestData.Solver);

        Assert.Equal(["Alice", "Bob, Jr."], result.Players.Select(p => p.Nickname));
        Assert.Equal(2, result.Players[0].Games.Count);
        Assert.Equal(["game-a1", "game-a2"], result.Players[0].Games.Select(g => g.Game.GameId));
        Assert.Equal(2, result.Skipped.Count);
        Assert.Contains(result.Skipped, s => s.GameId == "game-b2" && s.Reason.StartsWith("Not finished"));
        Assert.Contains(result.Skipped, s => s.GameId == "game-b3" && s.Reason.StartsWith("Single"));
        Assert.True(result.Players[0].AverageCost < 1e-6);
        Assert.True(result.Players[1].AverageCost > 0);

        string output = Path.Combine(dir, "analysis");
        var files = ReportWriter.WriteAll(result, output);
        Assert.Equal(
            ["alice-story.html", "alice.html", "alice.md", "bob-jr-story.html", "bob-jr.html", "bob-jr.md", "decisions.csv", "index.html"],
            files.Select(Path.GetFileName).Order(StringComparer.Ordinal));

        string index = File.ReadAllText(Path.Combine(output, "index.html"));
        Assert.Contains("Alice", index);
        Assert.Contains("Bob, Jr.", index);
        Assert.Contains("bob-jr.html", index);
        Assert.DoesNotContain("<script", index, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("http", index, StringComparison.OrdinalIgnoreCase);

        var csv = Csv.Parse(File.ReadAllText(Path.Combine(output, "decisions.csv")));
        int decisions = result.Players.Sum(p => p.Decisions.Count());
        Assert.Equal(decisions + 1, csv.Count);
        Assert.Equal("Bob, Jr.", csv.Last()[0]);
    }

    [Fact]
    public void SameExportAndSeedGiveIdenticalReports()
    {
        string dir = BuildExport();
        var options = new AnalysisOptions { Simulations = 30, Seed = 5 };
        var first = ReportWriter.WriteAll(AnalysisRunner.Run(ExportData.Load(dir), options, solver: TestData.Solver), Path.Combine(dir, "one"));
        var second = ReportWriter.WriteAll(AnalysisRunner.Run(ExportData.Load(dir), options, solver: TestData.Solver), Path.Combine(dir, "two"));

        Assert.Equal(first.Count, second.Count);
        foreach (var (a, b) in first.Zip(second))
        {
            Assert.Equal(Path.GetFileName(a), Path.GetFileName(b));
            Assert.Equal(File.ReadAllBytes(a), File.ReadAllBytes(b));
        }
    }

    [Fact]
    public void SolverCacheRoundTrips()
    {
        string path = Path.Combine(TestData.TempDirectory(), "cache", "card-values.bin");
        var computed = CardSolver.LoadOrCompute(DiceTables.Shared, path);
        Assert.True(File.Exists(path));
        string? message = null;
        var loaded = CardSolver.LoadOrCompute(DiceTables.Shared, path, m => message = m);
        Assert.StartsWith("Card values loaded", message);
        Assert.Equal(computed.StartValue, loaded.StartValue);
        Assert.Equal(computed.Value(new CardPosition(0b101010101, 30)), loaded.Value(new CardPosition(0b101010101, 30)));
    }
}

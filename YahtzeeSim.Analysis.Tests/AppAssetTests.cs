using System.Text.RegularExpressions;
using YahtzeeSim.Analysis.Data;
using YahtzeeSim.Analysis.Reports;
using YahtzeeSim.Analysis.Solver;

namespace YahtzeeSim.Analysis.Tests;

/// <summary>
/// The app ships a precomputed solver table and reference simulations (YahtzeeSim.Web/wwwroot/analysis). These tests
/// fail when they no longer match what the code computes; regenerate them with
/// <c>dotnet run --project YahtzeeSim.Analysis.Cli -c Release -- --write-app-assets YahtzeeSim.Web/wwwroot/analysis</c>.
/// </summary>
public class AppAssetTests
{
    private static string AssetDirectory()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "YahtzeeSim.sln")))
        {
            dir = dir.Parent;
        }

        Assert.NotNull(dir);
        return Path.Combine(dir!.FullName, "YahtzeeSim.Web", "wwwroot", "analysis");
    }

    [Fact]
    public void ShippedCardValuesMatchTheSolver()
    {
        using var expected = new MemoryStream();
        TestData.Solver.WriteAsset(expected, DiceTables.Shared);
        byte[] shipped = File.ReadAllBytes(Path.Combine(AssetDirectory(), "card-values.bin"));
        Assert.True(expected.ToArray().AsSpan().SequenceEqual(shipped), "card-values.bin is stale; regenerate it with --write-app-assets.");
    }

    [Fact]
    public void ShippedReferenceDataMatchesTheSimulation()
    {
        string shipped = File.ReadAllText(Path.Combine(AssetDirectory(), "reference.json"));
        var data = ReferenceData.FromJson(shipped, DiceTables.Shared);
        Assert.Equal(AnalysisOptions.DefaultSeed, data.Seed);
        Assert.Equal(AnalysisOptions.DefaultSimulations, data.Simulations);
        string expected = ReferenceData.Simulate(TestData.Evaluator, data.Simulations, data.Seed).ToJson();
        Assert.True(expected == shipped, "reference.json is stale; regenerate it with --write-app-assets.");
    }

    [Fact]
    public void CardValuesRoundTripThroughTheAssetFormat()
    {
        using var stream = new MemoryStream();
        TestData.Solver.WriteAsset(stream, DiceTables.Shared);
        stream.Position = 0;
        var loaded = CardSolver.ReadAsset(stream, DiceTables.Shared);
        foreach (var position in new[] { new CardPosition(0, 0), new CardPosition(0b1010101, 21), new CardPosition(0b111111, 70), new CardPosition(0x1FFE, 5) })
        {
            Assert.Equal(TestData.Solver.Value(position), loaded.Value(position));
        }
    }

    [Fact]
    public void AssetsBuiltForOtherRulesAreRejected()
    {
        using var stream = new MemoryStream();
        TestData.Solver.WriteAsset(stream, DiceTables.Shared);
        byte[] bytes = stream.ToArray();
        bytes[8] ^= 0xFF;
        Assert.Throws<InvalidDataException>(() => CardSolver.ReadAsset(new MemoryStream(bytes), DiceTables.Shared));
        Assert.Throws<InvalidDataException>(() => CardSolver.ReadAsset(new MemoryStream(bytes[..100]), DiceTables.Shared));

        var reference = ReferenceData.Simulate(TestData.Evaluator, 2, 1) with { RulesHash = 42 };
        Assert.Throws<InvalidDataException>(() => ReferenceData.FromJson(reference.ToJson(), DiceTables.Shared));
    }

    [Fact]
    public void PlayerFragmentIsSelfContainedAndScoped()
    {
        var game = TestData.BotGame(PlacementScoring.Greedy, 51, "<b>Eve</b>", "user-e", "game-e");
        var data = new ExportData([], [game.Score], game.Actions);
        var reference = ReferenceData.Simulate(TestData.Evaluator, 10, 3);
        var result = AnalysisRunner.Run(data, new AnalysisOptions(), solver: TestData.Solver, referenceData: reference);
        string html = HtmlReport.PlayerFragment(result, result.Players.Single());

        Assert.StartsWith("<style>", html);
        Assert.Contains("class=\"sr sr-embedded\"", html);
        Assert.DoesNotContain("<html", html);
        Assert.DoesNotContain("<script", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("href=", html);
        Assert.DoesNotContain("<b>Eve</b>", html);
        Assert.Contains("How this report works", html);
        Assert.Contains("seed 3", html);

        string css = html[7..html.IndexOf("</style>", StringComparison.Ordinal)];
        foreach (Match m in Regex.Matches(css, @"(?:^|[{}])\s*([^{}]+)\{"))
        {
            string prelude = m.Groups[1].Value.Trim();
            if (prelude.StartsWith('@'))
            {
                continue;
            }

            foreach (string selector in prelude.Split(','))
            {
                Assert.True(selector.Trim().StartsWith(".sr", StringComparison.Ordinal), $"unscoped selector '{selector.Trim()}'");
            }
        }
    }
}

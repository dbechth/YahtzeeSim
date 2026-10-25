using YahtzeeSim.Analysis.Data;
using YahtzeeSim.Analysis.Grading;
using YahtzeeSim.Analysis.Replay;
using YahtzeeSim.Analysis.Reports;
using YahtzeeSim.Analysis.Rules;
using YahtzeeSim.Analysis.Solver;
using YahtzeeSim.Analysis.Story;
using YahtzeeSim.Engine;

namespace YahtzeeSim.Analysis.Tests;

public class StoryTests
{
    [Theory]
    [InlineData(new[] { 4, 4, 4, 4, 4 }, RollSituation.Yahtzee)]
    [InlineData(new[] { 2, 2, 2, 2, 5 }, RollSituation.FourOfAKind)]
    [InlineData(new[] { 3, 3, 3, 6, 6 }, RollSituation.FullHouse)]
    [InlineData(new[] { 6, 2, 3, 4, 5 }, RollSituation.LargeStraight)]
    [InlineData(new[] { 1, 2, 3, 4, 6 }, RollSituation.FourInARow)]
    [InlineData(new[] { 3, 4, 4, 5, 6 }, RollSituation.FourInARow)]
    [InlineData(new[] { 1, 3, 3, 3, 6 }, RollSituation.ThreeOfAKind)]
    [InlineData(new[] { 2, 2, 4, 6, 6 }, RollSituation.TwoPair)]
    [InlineData(new[] { 1, 3, 4, 5, 5 }, RollSituation.PairAndRun)]
    [InlineData(new[] { 1, 2, 5, 6, 6 }, RollSituation.Pair)]
    [InlineData(new[] { 1, 2, 3, 5, 5 }, RollSituation.PairAndRun)]
    [InlineData(new[] { 1, 2, 3, 5, 6 }, RollSituation.ThreeInARow)]
    [InlineData(new[] { 1, 2, 4, 5, 1 }, RollSituation.Pair)]
    [InlineData(new[] { 1, 3, 5, 6, 2 }, RollSituation.ThreeInARow)]
    public void RollsAreClassifiedByFixedRules(int[] dice, RollSituation expected)
    {
        Assert.Equal(expected, KeepMoments.ClassifyRoll(dice));
    }

    [Theory]
    [InlineData(new[] { 1, 2, 3, 4, 5 }, KeepResponse.Stopped)]
    [InlineData(new int[0], KeepResponse.RerolledAll)]
    [InlineData(new[] { 6 }, KeepResponse.KeptOneDie)]
    [InlineData(new[] { 5, 5 }, KeepResponse.KeptMatching)]
    [InlineData(new[] { 2, 2, 2, 2 }, KeepResponse.KeptMatching)]
    [InlineData(new[] { 2, 3, 4 }, KeepResponse.KeptStraight)]
    [InlineData(new[] { 1, 3, 4, 5 }, KeepResponse.KeptStraight)]
    [InlineData(new[] { 1, 6, 3 }, KeepResponse.KeptMixed)]
    [InlineData(new[] { 5, 6 }, KeepResponse.KeptMixed)]
    [InlineData(new[] { 3, 3, 5 }, KeepResponse.KeptMixed)]
    public void KeepsAreClassifiedByFixedRules(int[] kept, KeepResponse expected)
    {
        Assert.Equal(expected, KeepMoments.ClassifyResponse(kept));
    }

    [Fact]
    public void EveryRollBeforeTheThirdIsOneMoment()
    {
        var (score, actions) = TestData.BotGame(PlacementScoring.CardValues, 61);
        var game = GameReplayer.Replay(score, actions);
        var moments = KeepMoments.From([game]);
        Assert.Equal(game.Turns.Sum(t => t.Rolls.Count(r => r.RollNumber < 3)), moments.Count);
        foreach (var m in moments)
        {
            var remaining = m.Dice.ToList();
            Assert.All(m.Kept, d => Assert.True(remaining.Remove(d), $"kept {d} was not showing"));
        }

        // A turn that ends before roll 3 has a final moment that keeps all five dice.
        foreach (var turn in game.Turns.Where(t => t.Rolls.Count < 3))
        {
            Assert.Contains(moments, m => m.Turn == turn.Turn && m.RollNumber == turn.Rolls.Count && m.Response == KeepResponse.Stopped);
        }
    }

    [Fact]
    public void RollLineWordingFollowsTheThresholds()
    {
        var lines = StoryRules.RollLines(
        [
            .. Moments(RollSituation.TwoPair, [2, 2, 4, 6, 6], (KeepResponse.KeptMatching, 8), (KeepResponse.KeptOneDie, 2)),
            .. Moments(RollSituation.Pair, [1, 2, 5, 6, 6], (KeepResponse.KeptMatching, 6), (KeepResponse.RerolledAll, 3)),
            .. Moments(RollSituation.ThreeOfAKind, [1, 3, 3, 3, 6], (KeepResponse.KeptMatching, 4), (KeepResponse.KeptOneDie, 4)),
            .. Moments(RollSituation.FourInARow, [1, 2, 3, 4, 6], (KeepResponse.KeptStraight, 3), (KeepResponse.KeptMatching, 2), (KeepResponse.KeptOneDie, 2)),
            .. Moments(RollSituation.Nothing, [1, 2, 4, 5, 6], (KeepResponse.RerolledAll, 3)),
            .. Moments(RollSituation.Yahtzee, [4, 4, 4, 4, 4], (KeepResponse.Stopped, 20)),
        ]);

        Assert.Equal(4, lines.Count);
        Assert.Equal([RollSituation.TwoPair, RollSituation.Pair, RollSituation.ThreeOfAKind, RollSituation.FourInARow], lines.Select(l => l.Situation));
        Assert.Equal("you almost always keep one pair and roll for more: 8 of 10 times.", lines[0].Text);
        Assert.Equal("you usually keep the pair: 6 of 9 times.", lines[1].Text);
        Assert.Equal("you more often than not keep the three and roll for more: 4 of 8 times.", lines[2].Text);
        Assert.Equal("you mix it up. Most often you go for the straight (3 of 7), otherwise you keep a pair instead (2).", lines[3].Text);
    }

    [Fact]
    public void RareSituationsAndRoll2AreLeftOut()
    {
        var moments = Moments(RollSituation.FullHouse, [3, 3, 3, 6, 6], (KeepResponse.Stopped, 3))
            .Concat(Moments(RollSituation.Pair, [1, 2, 5, 6, 6], (KeepResponse.KeptMatching, 9)).Select(m => m with { RollNumber = 2 }));
        Assert.Empty(StoryRules.RollLines(moments));
    }

    [Fact]
    public void TipComesFromTheCostliestFiredHabitRule()
    {
        var findings = new List<RuleFinding>
        {
            Finding("BIG-BOX-COST", true, 99),
            Finding("CHANCE-LOW", true, 12),
            Finding("KEPT-TOO-FEW", true, 20),
            Finding("UPPER-BELOW-PAR", false, 50),
        };
        var tip = StoryRules.Tip(findings);
        Assert.Equal("KEPT-TOO-FEW", tip.RuleId);
        Assert.Equal(StoryRules.Tips["KEPT-TOO-FEW"], tip.Text);
        Assert.Equal("On turn 9 you kept 3 3; keeping 2 3 4 was the stronger play by about 3 points on average.", tip.Example);

        var none = StoryRules.Tip([Finding("BIG-BOX-COST", true, 99)]);
        Assert.Equal(StoryRules.NoTip, none.Text);
        Assert.Null(none.Example);
    }

    [Fact]
    public void EveryTipBelongsToARealRule()
    {
        var ids = StrategyRules.All.Select(r => r.Id).ToHashSet();
        Assert.All(StoryRules.Tips.Keys, id => Assert.Contains(id, ids));
        Assert.DoesNotContain("BIG-BOX-COST", StoryRules.Tips.Keys);
    }

    [Fact]
    public void StoryIsDeterministicAndRendersAsScopedHtml()
    {
        var player = AnalysePlayer(PlacementScoring.Greedy, 71);
        var first = StoryReport.Fragment(StoryRules.Build(player));
        var second = StoryReport.Fragment(StoryRules.Build(AnalysePlayer(PlacementScoring.Greedy, 71)));
        Assert.Equal(first, second);
        Assert.StartsWith("<style>.ps{", first);
        Assert.DoesNotContain("<script", first, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("href=", first);
        Assert.Contains("Your play style", first);
        Assert.Contains("One thing to try", first);

        string css = first[7..first.IndexOf("</style>", StringComparison.Ordinal)];
        foreach (string rule in css.Split('}', StringSplitOptions.RemoveEmptyEntries))
        {
            string selectors = rule.Split('{')[0].Trim();
            foreach (string selector in selectors.Split(','))
            {
                Assert.StartsWith(".ps", selector.Trim());
            }
        }
    }

    private static PlayerAnalysis AnalysePlayer(PlacementScoring scoring, int seed)
    {
        var game = TestData.BotGame(scoring, seed, "Pat", "user-p", "game-p");
        var result = AnalysisRunner.Run(
            new ExportData([], [game.Score], game.Actions),
            new AnalysisOptions(),
            solver: TestData.Solver,
            referenceData: ReferenceData.Simulate(TestData.Evaluator, 5, 1));
        return result.Players.Single();
    }

    private static IEnumerable<KeepMoment> Moments(RollSituation situation, int[] dice, params (KeepResponse Response, int Count)[] responses)
    {
        int turn = 0;
        foreach (var (response, count) in responses)
        {
            for (int i = 0; i < count; i++)
            {
                yield return new KeepMoment("g", ++turn, 1, dice, response == KeepResponse.Stopped ? dice : [dice[^1], dice[^1]], situation, response);
            }
        }
    }

    private static RuleFinding Finding(string id, bool fired, double lost)
    {
        var rule = StrategyRules.All.Single(r => r.Id == id);
        var example = new GradedDecision("g", "Pat", 9, 1, DecisionKind.KeepAfterRoll1, "23336", "Keep 33", "Keep 234", 10, 13, KeepPattern.Pair, KeepPattern.StraightDraw, null, null, 1);
        return new RuleFinding(rule, new RuleMatch(3, 10, lost, [example]), fired, "x");
    }
}

public class BadgeTests
{
    [Fact]
    public void StrongestQualifyingBadgeWins()
    {
        var m = Metrics(choiceRolls: 40, keptMatching: 30, referenceMatching: 20, bonuses: 12, cards: 12);
        var result = BadgeRules.Choose(m);
        // Matching lean +25 pts (strength 2.1) beats Bonus Banker 100% (strength 2.0).
        Assert.Equal("yahtzee-hunter", result.Badge.Id);
        Assert.True(result.Scores.Single(s => s.Badge.Id == "bonus-banker").Qualifies);
    }

    [Fact]
    public void NothingQualifyingGivesAllRounder()
    {
        var result = BadgeRules.Choose(Metrics());
        Assert.Equal(BadgeRules.AllRounderId, result.Badge.Id);
        Assert.DoesNotContain(result.Scores, s => s.Qualifies);
    }

    [Fact]
    public void TooFewChancesNeverQualify()
    {
        var m = Metrics(choiceRolls: 10, keptMatching: 10, referenceMatching: 2);
        var result = BadgeRules.Choose(m);
        Assert.False(result.Scores.Single(s => s.Badge.Id == "yahtzee-hunter").Qualifies);
        Assert.Equal(BadgeRules.AllRounderId, result.Badge.Id);
    }

    [Fact]
    public void EqualStrengthsGoToTheEarlierBadge()
    {
        // Card Closer at 2.0 cards (0.5 under 2.5, scale 0.5) and Quick Draw at 4 s (2 under 6, scale 2) both have strength 1.
        var result = BadgeRules.Choose(Metrics(cardsInProgress: 2.0, medianSeconds: 4.0));
        Assert.Equal(1.0, result.Scores.Single(s => s.Badge.Id == "card-closer").Strength, 12);
        Assert.Equal(1.0, result.Scores.Single(s => s.Badge.Id == "quick-draw").Strength, 12);
        Assert.Equal("card-closer", result.Badge.Id);
    }

    [Theory]
    [InlineData(4.0, "quick-draw")]
    [InlineData(20.0, "deep-thinker")]
    public void TimingBadges(double seconds, string expected)
    {
        Assert.Equal(expected, BadgeRules.Choose(Metrics(medianSeconds: seconds)).Badge.Id);
    }

    [Fact]
    public void ServerAllowlistMatchesTheBadgeTable()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "YahtzeeSim.sln")))
        {
            dir = dir.Parent;
        }

        string ts = File.ReadAllText(Path.Combine(dir!.FullName, "functions", "_lib", "badges.ts"));
        int start = ts.IndexOf("STYLE_BADGES = [", StringComparison.Ordinal);
        string list = ts[start..ts.IndexOf("] as const", start, StringComparison.Ordinal)];
        var serverIds = System.Text.RegularExpressions.Regex.Matches(list, "\"([a-z-]+)\"").Select(m => m.Groups[1].Value).Order().ToList();
        Assert.Equal(BadgeRules.AllWithDefault.Select(b => b.Id).Order().ToList(), serverIds);
    }

    [Fact]
    public void BadgesHaveUniqueIdsAndDescriptions()
    {
        var all = BadgeRules.AllWithDefault.ToList();
        Assert.Equal(all.Count, all.Select(b => b.Id).Distinct().Count());
        Assert.All(all, b => Assert.False(string.IsNullOrWhiteSpace(b.Description)));
        Assert.All(all, b => Assert.Matches("^[a-z-]+$", b.Id));
        Assert.Same(BadgeRules.AllRounder, BadgeRules.Find(BadgeRules.AllRounderId));
        Assert.Null(BadgeRules.Find("best-player"));
    }

    [Fact]
    public void ReferenceRatesStillMatchTheReferenceStrategy()
    {
        var games = Enumerable.Range(0, 60).Select(i =>
        {
            var (score, actions) = TestData.BotGame(PlacementScoring.CardValues, 9000 + i, gameId: "r" + i);
            return GameReplayer.Replay(score, actions);
        }).ToList();
        var moments = KeepMoments.From(games);
        foreach (var situation in new[] { RollSituation.FourInARow, RollSituation.PairAndRun })
        {
            var list = moments.Where(m => m.Situation == situation && m.Response != KeepResponse.Stopped).ToList();
            double matching = (double)list.Count(m => m.Response == KeepResponse.KeptMatching) / list.Count;
            double straight = (double)list.Count(m => m.Response == KeepResponse.KeptStraight) / list.Count;
            Assert.InRange(matching, BadgeRules.ReferenceMatchingRate(situation) - 0.05, BadgeRules.ReferenceMatchingRate(situation) + 0.05);
            Assert.InRange(straight, BadgeRules.ReferenceStraightRate(situation) - 0.05, BadgeRules.ReferenceStraightRate(situation) + 0.05);
        }
    }

    [Fact]
    public void ReferenceStrategyIsNotAHunterOrShooter()
    {
        var reference = StyleReference.Simulate(TestData.Evaluator, 30, 4);
        Assert.InRange(reference.MatchingLean, -0.05, 0.05);
        Assert.InRange(reference.StraightLean, -0.05, 0.05);
    }

    private static StyleMetrics Metrics(
        int choiceRolls = 40,
        int keptMatching = 20,
        double referenceMatching = 20,
        int keptStraight = 10,
        double referenceStraight = 10,
        int bonuses = 5,
        int cards = 6,
        double cardsInProgress = 3.5,
        double earlyStops = 6,
        double medianSeconds = 9) =>
        new(cards / YahtzeeGameEngine.GameCount, cards, 78, choiceRolls, keptMatching, keptStraight, referenceMatching, referenceStraight,
            bonuses, cardsInProgress, (int)earlyStops, medianSeconds, 0);
}

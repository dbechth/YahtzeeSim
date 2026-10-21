using YahtzeeSim.Analysis.Grading;
using YahtzeeSim.Analysis.Profile;
using YahtzeeSim.Analysis.Replay;
using YahtzeeSim.Analysis.Rules;
using YahtzeeSim.Analysis.Solver;
using YahtzeeSim.Engine;

namespace YahtzeeSim.Analysis.Tests;

public class RulesTests
{
    private static readonly Lazy<StrategyProfile> BaseProfile = new(() =>
    {
        var (score, actions) = TestData.BotGame(PlacementScoring.CardValues, 41);
        var graded = new DecisionGrader(TestData.Evaluator).Grade(GameReplayer.Replay(score, actions));
        return StrategyProfile.Build([graded], ScoreRules.ScoringCategories.ToDictionary(c => c, _ => 0.0)) with { UpperBonuses = 6, Undos = 0, UndosAfterNextRoll = 0 };
    });

    private static readonly SimulationSummary Reference = new("Reference strategy", 1, 6, [1650], [1650], new double[13], 0.95);

    [Fact]
    public void UpperBelowParFiresAtFourCasesAndAQuarter()
    {
        var decisions = Enumerable.Range(1, 10)
            .Select(t => Box(t, ScoreCategory.Fives, t <= 4 ? 10 : 15, cost: t <= 4 ? 2 : 0))
            .ToList();
        var finding = Find("UPPER-BELOW-PAR", decisions);
        Assert.True(finding.Fired);
        Assert.Equal(4, finding.Match.Cases);
        Assert.Equal(10, finding.Match.Opportunities);
        Assert.Equal(8, finding.Match.PointsLost, 9);
        Assert.Contains("4 times out of 10 (40%)", finding.Sentence);
        Assert.Contains("8.0 points", finding.Sentence);
    }

    [Fact]
    public void UpperBelowParIgnoresLateTurnsAndStaysQuietBelowThreshold()
    {
        var decisions = new List<GradedDecision>
        {
            Box(10, ScoreCategory.Sixes, 12),
            Box(11, ScoreCategory.Sixes, 12),
            Box(12, ScoreCategory.Sixes, 12),
            Box(70, ScoreCategory.Sixes, 6),
            Box(71, ScoreCategory.Sixes, 6),
        };
        var finding = Find("UPPER-BELOW-PAR", decisions);
        Assert.Equal(3, finding.Match.Cases);
        Assert.Equal(3, finding.Match.Opportunities);
        Assert.False(finding.Fired);
    }

    [Fact]
    public void YahtzeeScratchFiresOnlyWithThirtyTurnsLeft()
    {
        Assert.True(Find("YAHTZEE-SCRATCHED-EARLY", [Box(48, ScoreCategory.Yahtzee, 0)]).Fired);
        Assert.False(Find("YAHTZEE-SCRATCHED-EARLY", [Box(49, ScoreCategory.Yahtzee, 0)]).Fired);
        Assert.False(Find("YAHTZEE-SCRATCHED-EARLY", [Box(5, ScoreCategory.Yahtzee, 50)]).Fired);
    }

    [Fact]
    public void AvoidableScratchNeedsAScoringReferenceBoxAndCost()
    {
        var decisions = new List<GradedDecision>
        {
            Box(5, ScoreCategory.LargeStraight, 0, cost: 3, best: ScoreCategory.Chance),
            Box(6, ScoreCategory.LargeStraight, 0, cost: 4, best: ScoreCategory.Chance),
            Box(7, ScoreCategory.Yahtzee, 0, cost: 5, best: ScoreCategory.Aces, bestScratch: true),
            Box(8, ScoreCategory.FullHouse, 0, cost: 1, best: ScoreCategory.Chance),
        };
        var finding = Find("SCRATCH-AVOIDABLE", decisions);
        Assert.True(finding.Fired);
        Assert.Equal(2, finding.Match.Cases);
        Assert.Equal(4, finding.Match.Opportunities);
    }

    [Fact]
    public void LowChanceFiresAtTwoCases()
    {
        Assert.False(Find("CHANCE-LOW", [Box(1, ScoreCategory.Chance, 15), Box(2, ScoreCategory.Chance, 25)]).Fired);
        Assert.True(Find("CHANCE-LOW", [Box(1, ScoreCategory.Chance, 15), Box(2, ScoreCategory.Chance, 19)]).Fired);
    }

    [Fact]
    public void StoppedEarlyCountsOnlyCostlyStops()
    {
        var decisions = new List<GradedDecision>
        {
            Keep(DecisionKind.StopAfterRoll1, KeepPattern.AllFive, KeepPattern.Pair, 1.5, 5, 2),
            Keep(DecisionKind.StopAfterRoll2, KeepPattern.AllFive, KeepPattern.Pair, 2, 5, 2),
            Keep(DecisionKind.StopAfterRoll2, KeepPattern.AllFive, KeepPattern.Pair, 0.2, 5, 2),
        };
        var finding = Find("STOPPED-EARLY", decisions);
        Assert.Equal(2, finding.Match.Cases);
        Assert.False(finding.Fired);
        decisions.Add(Keep(DecisionKind.StopAfterRoll1, KeepPattern.AllFive, KeepPattern.Single, 3, 5, 1));
        Assert.True(Find("STOPPED-EARLY", decisions).Fired);
    }

    [Fact]
    public void KindOverStraightAndTheReverse()
    {
        var decisions = new List<GradedDecision>
        {
            Keep(DecisionKind.KeepAfterRoll1, KeepPattern.Pair, KeepPattern.StraightDraw, 1),
            Keep(DecisionKind.KeepAfterRoll1, KeepPattern.ThreeOfAKind, KeepPattern.StraightDraw, 0.6),
            Keep(DecisionKind.KeepAfterRoll2, KeepPattern.Pair, KeepPattern.StraightDraw, 2),
            Keep(DecisionKind.KeepAfterRoll2, KeepPattern.Pair, KeepPattern.StraightDraw, 0.1),
            Keep(DecisionKind.KeepAfterRoll1, KeepPattern.StraightDraw, KeepPattern.Pair, 2),
        };
        var kind = Find("KIND-OVER-STRAIGHT", decisions);
        Assert.True(kind.Fired);
        Assert.Equal(3, kind.Match.Cases);
        Assert.Equal(4, kind.Match.Opportunities);
        Assert.False(Find("STRAIGHT-OVER-KIND", decisions).Fired);
    }

    [Fact]
    public void KeptTooFewAndTooMany()
    {
        var fewer = Enumerable.Range(0, 5).Select(_ => Keep(DecisionKind.KeepAfterRoll1, KeepPattern.Single, KeepPattern.Pair, 1.2, 1, 2)).ToList();
        Assert.True(Find("KEPT-TOO-FEW", fewer).Fired);
        Assert.False(Find("KEPT-TOO-MANY", fewer).Fired);
        Assert.False(Find("KEPT-TOO-FEW", fewer.Take(4).ToList()).Fired);
    }

    [Fact]
    public void SameBoxOtherCardNeedsTheSameCategory()
    {
        var decisions = new List<GradedDecision>
        {
            Box(10, ScoreCategory.Fours, 12, cost: 3, card: 0, bestCard: 1),
            Box(11, ScoreCategory.Fours, 12, cost: 3, card: 2, bestCard: 1),
            Box(12, ScoreCategory.Fours, 12, cost: 3, card: 3, bestCard: 4),
            Box(13, ScoreCategory.Fours, 12, cost: 3, card: 3, bestCard: 4, best: ScoreCategory.Chance),
        };
        var finding = Find("SAME-BOX-OTHER-CARD", decisions);
        Assert.True(finding.Fired);
        Assert.Equal(3, finding.Match.Cases);
    }

    [Fact]
    public void BonusRuleFiresAboveOneCardInSix()
    {
        var profile = BaseProfile.Value with { Cards = 12, UpperBonuses = 10 };
        Assert.False(Evaluate([], profile).Single(f => f.Rule.Id == "UPPER-BONUS-MISSED").Fired);
        profile = profile with { UpperBonuses = 9 };
        var finding = Evaluate([], profile).Single(f => f.Rule.Id == "UPPER-BONUS-MISSED");
        Assert.True(finding.Fired);
        Assert.Contains("3 of 12 cards (25%)", finding.Sentence);
        Assert.Contains("misses it on 5% of cards", finding.Sentence);
    }

    [Fact]
    public void UndoAfterNextRollFiresOnce()
    {
        var profile = BaseProfile.Value with { Undos = 3, UndosAfterNextRoll = 1 };
        Assert.True(Evaluate([], profile).Single(f => f.Rule.Id == "UNDO-AFTER-NEXT-ROLL").Fired);
    }

    [Fact]
    public void FiredFindingsComeFirstByPointsLost()
    {
        var decisions = new List<GradedDecision>
        {
            Box(10, ScoreCategory.Yahtzee, 0, cost: 2),
            Box(20, ScoreCategory.Chance, 12, cost: 4),
            Box(21, ScoreCategory.Chance, 13, cost: 4),
        };
        var findings = Evaluate(decisions, BaseProfile.Value);
        Assert.Equal(StrategyRules.All.Count, findings.Count);
        Assert.Equal("CHANCE-LOW", findings[0].Rule.Id);
        Assert.Equal("YAHTZEE-SCRATCHED-EARLY", findings[1].Rule.Id);
        Assert.All(findings.Skip(2), f => Assert.False(f.Fired));
    }

    [Fact]
    public void RuleIdsAreUniqueAndTemplatesUseOnlyKnownPlaceholders()
    {
        Assert.Equal(StrategyRules.All.Count, StrategyRules.All.Select(r => r.Id).Distinct().Count());
        foreach (var rule in StrategyRules.All)
        {
            string stripped = rule.Template;
            foreach (var p in new[] { "{cases}", "{opportunities}", "{rate}", "{lost}", "{reference}" })
            {
                stripped = stripped.Replace(p, string.Empty);
            }

            Assert.DoesNotContain("{", stripped);
        }
    }

    private static RuleFinding Find(string id, List<GradedDecision> decisions) =>
        Evaluate(decisions, BaseProfile.Value).Single(f => f.Rule.Id == id);

    private static IReadOnlyList<RuleFinding> Evaluate(List<GradedDecision> decisions, StrategyProfile profile) =>
        StrategyRules.Evaluate(new RuleContext(decisions, profile, Reference));

    private static GradedDecision Box(
        int turn,
        ScoreCategory category,
        int points,
        double cost = 0,
        ScoreCategory? best = null,
        int card = 0,
        int bestCard = 0,
        bool bestScratch = false)
    {
        var chosen = new BoxChoice(card, category, points, points == 0);
        var bestBox = new BoxChoice(bestCard, best ?? category, bestScratch ? 0 : 10, bestScratch);
        return new GradedDecision("game", "Player", turn, 3, DecisionKind.Box, "12345", chosen.Text, bestBox.Text, 10 - cost, 10, null, null, chosen, bestBox, 1);
    }

    private static GradedDecision Keep(DecisionKind kind, KeepPattern chosen, KeepPattern best, double cost, int chosenSize = 2, int bestSize = 2) =>
        new("game", "Player", 5, kind == DecisionKind.KeepAfterRoll2 || kind == DecisionKind.StopAfterRoll2 ? 2 : 1, kind, "12345", "Keep", "Keep", 10 - cost, 10, chosen, best, null, null, 1)
        {
            ChosenKeepSize = chosenSize,
            BestKeepSize = bestSize
        };
}

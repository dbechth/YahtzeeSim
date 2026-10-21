using YahtzeeSim.Analysis.Grading;
using YahtzeeSim.Analysis.Solver;
using YahtzeeSim.Engine;

namespace YahtzeeSim.Analysis.Profile;

/// <summary>Per-box results across all of a player's cards.</summary>
public sealed record CategoryStats(
    ScoreCategory Category,
    int Fills,
    double AveragePoints,
    int Scratches,
    double AverageTurn,
    double AverageReferencePoints)
{
    public double ScratchRate => Fills == 0 ? 0 : (double)Scratches / Fills;

    /// <summary>Upper boxes only: average count of the face (3 is par for the 63-point bonus).</summary>
    public double AverageCount => Category < ScoreCategory.ThreeOfAKind ? AveragePoints / ((int)Category + 1) : 0;
}

/// <summary>How often a keep pattern was chosen, next to how often the reference strategy chose it in the same spots.</summary>
public sealed record PatternCount(KeepPattern Pattern, int Player, int Reference);

/// <summary>Decision quality for one kind of decision.</summary>
public sealed record DecisionStats(DecisionKind Kind, int Count, int Matches, double TotalCost, double MedianSeconds)
{
    public double MatchRate => Count == 0 ? 0 : (double)Matches / Count;

    public double AverageCost => Count == 0 ? 0 : TotalCost / Count;
}

/// <summary>Descriptive statistics for one player, computed only by counting and averaging the replayed games.</summary>
public sealed record StrategyProfile(
    int Games,
    int Cards,
    IReadOnlyList<CategoryStats> Categories,
    int UpperBonuses,
    double AverageUpperSubtotal,
    int[,] FillsByPhase,
    double AverageCardsInProgress,
    double CardSwitchRate,
    int[] TurnsByRollsUsed,
    IReadOnlyList<PatternCount> KeepsAfterRoll1,
    IReadOnlyList<PatternCount> KeepsAfterRoll2,
    IReadOnlyList<DecisionStats> Decisions,
    int ChangesOfMind,
    int Undos,
    int UndosChangedBox,
    int UndosAfterNextRoll,
    int YahtzeesScored,
    double MedianTurnSeconds)
{
    public const int Phases = 6;

    public const int TurnsPerPhase = 13;

    public double UpperBonusRate => Cards == 0 ? 0 : (double)UpperBonuses / Cards;

    public static StrategyProfile Build(IReadOnlyList<GradedGame> games, IReadOnlyDictionary<ScoreCategory, double> referencePoints)
    {
        var turns = games.SelectMany(g => g.Game.Turns).ToList();
        var decisions = games.SelectMany(g => g.Decisions).ToList();
        int cards = games.Count * YahtzeeGameEngine.GameCount;

        var categories = ScoreRules.ScoringCategories.Select(category =>
        {
            var fills = turns.Where(t => t.Box!.Category == category).ToList();
            return new CategoryStats(
                category,
                fills.Count,
                fills.Count == 0 ? 0 : fills.Average(t => t.Box!.Points),
                fills.Count(t => t.Box!.Scratch),
                fills.Count == 0 ? 0 : fills.Average(t => t.Turn),
                referencePoints.TryGetValue(category, out double r) ? r : 0);
        }).ToList();

        var phases = new int[Phases, DiceTables.CategoryCount];
        foreach (var t in turns)
        {
            phases[Math.Min(Phases - 1, (t.Turn - 1) / TurnsPerPhase), (int)t.Box!.Category]++;
        }

        double inProgress = 0;
        int switches = 0;
        int switchOpportunities = 0;
        foreach (var game in games)
        {
            int? previousCard = null;
            foreach (var t in game.Game.Turns)
            {
                inProgress += t.BoardBefore.Count(p => p.Mask != 0 && !p.IsComplete);
                if (previousCard is { } prev)
                {
                    switchOpportunities++;
                    if (prev != t.Box!.Card)
                    {
                        switches++;
                    }
                }

                previousCard = t.Box!.Card;
            }
        }

        var rollsUsed = new int[4];
        foreach (var t in turns)
        {
            rollsUsed[t.Rolls.Count]++;
        }

        var turnSeconds = turns
            .Select(t => (t.Box!.At - t.Rolls[0].At) / 1000.0)
            .Where(s => s >= 0)
            .Order()
            .ToList();

        return new StrategyProfile(
            games.Count,
            cards,
            categories,
            games.Sum(g => Enumerable.Range(0, YahtzeeGameEngine.GameCount).Count(g.Game.HasUpperBonus)),
            cards == 0 ? 0 : games.Average(g => Enumerable.Range(0, YahtzeeGameEngine.GameCount).Average(g.Game.UpperSubtotal)),
            phases,
            turns.Count == 0 ? 0 : inProgress / turns.Count,
            switchOpportunities == 0 ? 0 : (double)switches / switchOpportunities,
            rollsUsed,
            Patterns(decisions, DecisionKind.KeepAfterRoll1),
            Patterns(decisions, DecisionKind.KeepAfterRoll2),
            [.. Enum.GetValues<DecisionKind>().Select(kind =>
            {
                var list = decisions.Where(d => d.Kind == kind).ToList();
                return new DecisionStats(kind, list.Count, list.Count(d => d.MatchesReference), list.Sum(d => d.Cost), Median(list.Select(d => d.ThinkSeconds)));
            })],
            turns.Sum(t => t.Rolls.Sum(r => r.ChangesOfMind)),
            games.Sum(g => g.Game.UndoCount),
            games.Sum(g => g.Game.UndoChangedBox),
            games.Sum(g => g.Game.UndoAfterNextRoll),
            turns.Count(t => t.Box!.Category == ScoreCategory.Yahtzee && !t.Box.Scratch),
            Median(turnSeconds));
    }

    public static double Median(IEnumerable<double> values)
    {
        var sorted = values.Order().ToList();
        if (sorted.Count == 0)
        {
            return 0;
        }

        int mid = sorted.Count / 2;
        return sorted.Count % 2 == 1 ? sorted[mid] : (sorted[mid - 1] + sorted[mid]) / 2;
    }

    private static List<PatternCount> Patterns(List<GradedDecision> decisions, DecisionKind kind)
    {
        var relevant = decisions.Where(d => d.Kind == kind).ToList();
        return [.. Enum.GetValues<KeepPattern>()
            .Select(p => new PatternCount(p, relevant.Count(d => d.ChosenPattern == p), relevant.Count(d => d.BestPattern == p)))
            .Where(p => p.Player > 0 || p.Reference > 0)];
    }
}

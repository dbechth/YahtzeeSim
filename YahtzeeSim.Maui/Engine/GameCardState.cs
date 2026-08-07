using System.Collections.ObjectModel;

namespace YahtzeeSim.Maui.Engine;

public sealed class GameCardState
{
    public const int UpperBonusThreshold = 63;
    public const int UpperBonusValue = 35;

    public Dictionary<ScoreCategory, ScoreCellState> Scores { get; }

    public GameCardState()
    {
        Scores = new Dictionary<ScoreCategory, ScoreCellState>();
        foreach (var category in ScoreRules.ScoringCategories)
        {
            Scores[category] = new ScoreCellState();
        }
    }

    public GameCardState Clone()
    {
        var clone = new GameCardState();
        foreach (var category in ScoreRules.ScoringCategories)
        {
            clone.Scores[category] = Scores[category].Clone();
        }

        return clone;
    }

    public bool IsComplete => ScoreRules.ScoringCategories.All(c => Scores[c].IsFilled);

    public int? UpperSubtotal => IsComplete
        ? ScoreRules.UpperCategories.Sum(c => Scores[c].Value ?? 0)
        : null;

    public int? LowerSubtotal => IsComplete
        ? ScoreRules.LowerCategories.Sum(c => Scores[c].Value ?? 0)
        : null;

    public int? UpperBonus => IsComplete
        ? ((UpperSubtotal ?? 0) >= UpperBonusThreshold ? UpperBonusValue : 0)
        : null;

    public int? GameTotal => IsComplete
        ? (UpperSubtotal ?? 0) + (LowerSubtotal ?? 0) + (UpperBonus ?? 0)
        : null;
}

using System.Text.Json.Serialization;

namespace YahtzeeSim.Engine;

public sealed class GameCardState
{
    public const int UpperBonusThreshold = 63;
    public const int UpperBonusValue = 35;
    public const int YahtzeeBonusValue = 100;

    public Dictionary<ScoreCategory, ScoreCellState> Scores { get; set; }

    /// <summary>Number of check marks in the Yahtzee Bonus row (Single mode only).</summary>
    public int YahtzeeBonusCount { get; set; }

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
        var clone = new GameCardState { YahtzeeBonusCount = YahtzeeBonusCount };
        foreach (var category in ScoreRules.ScoringCategories)
        {
            clone.Scores[category] = Scores[category].Clone();
        }

        return clone;
    }

    [JsonIgnore]
    public bool IsComplete => ScoreRules.ScoringCategories.All(c => Scores[c].IsFilled);

    [JsonIgnore]
    public int YahtzeeBonusTotal => YahtzeeBonusCount * YahtzeeBonusValue;

    /// <summary>Upper boxes only, excluding the 35-point bonus.</summary>
    [JsonIgnore]
    public int? UpperSubtotal => IsComplete
        ? ScoreRules.UpperCategories.Sum(c => Scores[c].Value ?? 0)
        : null;

    /// <summary>Lower boxes plus any Yahtzee bonus.</summary>
    [JsonIgnore]
    public int? LowerSubtotal => IsComplete
        ? ScoreRules.LowerCategories.Sum(c => Scores[c].Value ?? 0) + YahtzeeBonusTotal
        : null;

    [JsonIgnore]
    public int? UpperBonus => IsComplete
        ? ((UpperSubtotal ?? 0) >= UpperBonusThreshold ? UpperBonusValue : 0)
        : null;

    [JsonIgnore]
    public int? GameTotal => IsComplete
        ? (UpperSubtotal ?? 0) + (LowerSubtotal ?? 0) + (UpperBonus ?? 0)
        : null;
}

namespace YahtzeeSim.Engine;

/// <summary>How a score cell can be used with the current dice.</summary>
public enum PickStatus
{
    /// <summary>The cell cannot be chosen right now.</summary>
    Unavailable,

    /// <summary>The dice are valid for the cell and score its value.</summary>
    Valid,

    /// <summary>The dice are not valid for the cell; it can only be recorded as a scratch (X, 0 points).</summary>
    Scratch
}

public readonly record struct ScoreResult(
    bool Applied,
    bool WasScratch,
    int Score,
    bool WasYahtzee,
    bool YahtzeeBonusAwarded);

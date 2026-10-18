namespace YahtzeeSim.Engine;

public sealed class ScoreCellState
{
    public int? Value { get; set; }
    public bool IsScratch { get; set; }
    public string PenColorHex { get; set; } = "#1D2939";

    public bool IsFilled => Value.HasValue;

    public string DisplayText => IsScratch ? "X" : (Value?.ToString() ?? string.Empty);

    public ScoreCellState Clone()
    {
        return new ScoreCellState
        {
            Value = Value,
            IsScratch = IsScratch,
            PenColorHex = PenColorHex
        };
    }
}

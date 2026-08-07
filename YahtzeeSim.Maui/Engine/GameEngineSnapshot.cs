namespace YahtzeeSim.Maui.Engine;

public sealed class GameEngineSnapshot
{
    public int RollCount { get; set; }
    public int ActiveCardIndex { get; set; }
    public GameMode Mode { get; set; }
    public List<DieState> Dice { get; set; } = [];
    public List<GameCardState> Cards { get; set; } = [];
}

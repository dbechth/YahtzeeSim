namespace YahtzeeSim.Engine;

public sealed class GameEngineSnapshot
{
    public int RollCount { get; set; }
    public GameMode Mode { get; set; }
    public List<DieState> Dice { get; set; } = [];
    public List<GameCardState> Cards { get; set; } = [];
}

namespace YahtzeeSim.Engine;

/// <summary>Everything needed to resume a game: the current state plus the one-level undo snapshot.</summary>
public sealed class GameSave
{
    public const int CurrentVersion = 1;

    public int Version { get; set; } = CurrentVersion;
    public GameEngineSnapshot Current { get; set; } = new();
    public GameEngineSnapshot? Undo { get; set; }
}

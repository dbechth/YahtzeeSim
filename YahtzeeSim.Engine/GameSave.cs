namespace YahtzeeSim.Engine;

/// <summary>Everything needed to resume a game: the current state plus the one-level undo snapshot.</summary>
public sealed class GameSave
{
    public const int CurrentVersion = 1;

    public int Version { get; set; } = CurrentVersion;
    /// <summary>Absent in saves written before accounts existed; the engine then assigns a fresh id.</summary>
    public Guid? GameId { get; set; }
    /// <summary>The challenge this game is being played for, if any (absent in older saves).</summary>
    public string? ChallengeId { get; set; }
    public GameEngineSnapshot Current { get; set; } = new();
    public GameEngineSnapshot? Undo { get; set; }
}

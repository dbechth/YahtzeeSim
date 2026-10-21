using System.Diagnostics.CodeAnalysis;
using YahtzeeSim.Analysis.Data;
using YahtzeeSim.Analysis.Solver;
using YahtzeeSim.Engine;

namespace YahtzeeSim.Analysis.Replay;

/// <summary>One roll within a turn. <see cref="Kept"/> marks the dice held from the previous roll (all false for roll 1).</summary>
public sealed record RollStep(int RollNumber, int[] Dice, bool[] Kept, int HoldPresses, int ChangesOfMind, long At)
{
    /// <summary>The kept dice values, in die order.</summary>
    public int[] KeptValues(int[] previousDice) => [.. Enumerable.Range(0, 5).Where(i => Kept[i]).Select(i => previousDice[i])];
}

/// <summary>Where a turn's dice were scored.</summary>
public sealed record ScoredBox(int Card, ScoreCategory Category, int Points, bool Scratch, long At);

/// <summary>One finished turn: the board before it, every roll, and where it was scored.</summary>
public sealed class ReplayedTurn
{
    public required int Turn { get; init; }

    public required CardPosition[] BoardBefore { get; init; }

    public List<RollStep> Rolls { get; } = [];

    public ScoredBox? Box { get; set; }

    public int[] FinalDice => Rolls[^1].Dice;
}

/// <summary>A finished Full Sheet game rebuilt from its action log and checked against the submitted total.</summary>
public sealed class ReplayedGame
{
    public required string GameId { get; init; }

    public required string UserId { get; init; }

    public required string Nickname { get; init; }

    public required int SubmittedTotal { get; init; }

    public required long PlayedAt { get; init; }

    public required IReadOnlyList<ReplayedTurn> Turns { get; init; }

    /// <summary>Final points per card per category (index [card][category]).</summary>
    public required int[][] CardScores { get; init; }

    public required int[] CardTotals { get; init; }

    public int Total => CardTotals.Sum();

    public int UndoCount { get; init; }

    /// <summary>Undos after which the turn was scored in a different box.</summary>
    public int UndoChangedBox { get; init; }

    /// <summary>Undos pressed after the next turn's dice were already rolled; those rolls were thrown away.</summary>
    public int UndoAfterNextRoll { get; init; }

    public int UpperSubtotal(int card) => CardScores[card].Take(6).Sum();

    public bool HasUpperBonus(int card) => UpperSubtotal(card) >= GameCardState.UpperBonusThreshold;
}

public sealed class ReplayException(string gameId, int seq, string message)
    : Exception($"Game {gameId}, action {seq}: {message}");

/// <summary>
/// Rebuilds a Full Sheet game from its actions. Hold state is reconstructed from Hold/Unhold presses; Undo restores the
/// turn as it was before the score (dropping any rolls of the following turn). Every step is checked against the rules,
/// and the final card totals must equal the submitted total.
/// </summary>
public static class GameReplayer
{
    private const int Cards = YahtzeeGameEngine.GameCount;

    public static ReplayedGame Replay(ScoreRow score, IEnumerable<ActionRow> actions)
    {
        string gameId = score.GameId;
        var ordered = actions.OrderBy(a => a.Seq).ToList();
        var cardScores = Enumerable.Range(0, Cards).Select(_ => new int?[DiceTables.CategoryCount]).ToArray();
        var completed = new List<(ReplayedTurn Turn, bool[] Held)>();
        var held = new bool[5];
        ReplayedTurn? current = null;
        int[]? dice = null;
        int presses = 0;
        int changes = 0;
        var pressedSinceRoll = new HashSet<int>();
        int undoCount = 0;
        int undoChanged = 0;
        int undoAfterRoll = 0;
        var undoneBoxes = new Dictionary<int, (int Card, ScoreCategory Category)>();
        int expectedSeq = 1;

        foreach (var a in ordered)
        {
            if (a.Seq != expectedSeq)
            {
                Fail(a, $"sequence number {a.Seq} found where {expectedSeq} was expected (missing actions)");
            }

            expectedSeq++;
            if (a.Mode != nameof(GameMode.FullSheet))
            {
                Fail(a, $"mode {a.Mode} is not FullSheet");
            }

            int nextTurn = completed.Count + 1;
            switch (a.Action)
            {
                case "Roll":
                {
                    int[] rolled = ParseDice(a);
                    if (current == null || current.Rolls.Count == 0)
                    {
                        if (a.RollNumber != 1)
                        {
                            Fail(a, $"first roll of a turn has roll number {a.RollNumber}");
                        }

                        current ??= NewTurn(nextTurn);
                        Array.Clear(held);
                    }
                    else
                    {
                        if (a.RollNumber != current.Rolls.Count + 1 || a.RollNumber > YahtzeeGameEngine.RollsPerTurn)
                        {
                            Fail(a, $"roll number {a.RollNumber} after {current.Rolls.Count} rolls");
                        }

                        for (int i = 0; i < 5; i++)
                        {
                            if (held[i] && rolled[i] != dice![i])
                            {
                                Fail(a, $"held die {i + 1} changed from {dice[i]} to {rolled[i]}");
                            }
                        }
                    }

                    if (a.Turn != current.Turn)
                    {
                        Fail(a, $"turn {a.Turn} logged during turn {current.Turn}");
                    }

                    var kept = current.Rolls.Count == 0 ? new bool[5] : (bool[])held.Clone();
                    current.Rolls.Add(new RollStep(a.RollNumber, rolled, kept, presses, changes, a.OccurredAt));
                    dice = rolled;
                    presses = 0;
                    changes = 0;
                    pressedSinceRoll.Clear();
                    if (a.RollNumber == YahtzeeGameEngine.RollsPerTurn)
                    {
                        Array.Fill(held, true);
                    }

                    break;
                }

                case "Hold":
                case "Unhold":
                {
                    if (current == null || current.Rolls.Count == 0 || dice == null)
                    {
                        Fail(a, "hold press before the first roll");
                    }

                    if (a.DieId is not (>= 1 and <= 5))
                    {
                        Fail(a, $"die id {a.DieId} is not 1..5");
                    }

                    CheckDice(a, dice!);
                    int index = a.DieId!.Value - 1;
                    held[index] = a.Action == "Hold";
                    presses++;
                    if (!pressedSinceRoll.Add(index))
                    {
                        changes++;
                    }

                    break;
                }

                case "Score":
                case "Scratch":
                {
                    if (current == null || current.Rolls.Count == 0 || dice == null)
                    {
                        Fail(a, "score before the first roll");
                    }

                    CheckDice(a, dice!);
                    if (a.RollNumber != current!.Rolls.Count || a.Turn != current.Turn)
                    {
                        Fail(a, $"score logged at turn {a.Turn} roll {a.RollNumber}, replay is at turn {current.Turn} roll {current.Rolls.Count}");
                    }

                    if (a.GameIndex is not (>= 0 and < Cards) || !Enum.TryParse<ScoreCategory>(a.Category, out var category) || a.Points == null)
                    {
                        Fail(a, "score without a valid card, category or points");
                    }

                    int card = a.GameIndex!.Value;
                    if (cardScores[card][(int)category] != null)
                    {
                        Fail(a, $"{category} on card {card + 1} is already filled");
                    }

                    var (valid, points) = ScoreRules.EvaluateCategory(category, dice!);
                    bool scratch = a.Action == "Scratch";
                    if (scratch == valid || (scratch ? 0 : points) != a.Points)
                    {
                        Fail(a, $"{a.Action} of {category} for {a.Points} points does not match the dice {a.Dice}");
                    }

                    if (undoneBoxes.Remove(current.Turn, out var undone) && (undone.Card != card || undone.Category != category))
                    {
                        undoChanged++;
                    }

                    cardScores[card][(int)category] = a.Points;
                    current.Box = new ScoredBox(card, category, a.Points!.Value, scratch, a.OccurredAt);
                    completed.Add((current, (bool[])held.Clone()));
                    current = null;
                    dice = null;
                    Array.Clear(held);
                    presses = 0;
                    changes = 0;
                    pressedSinceRoll.Clear();
                    break;
                }

                case "Undo":
                {
                    if (completed.Count == 0)
                    {
                        Fail(a, "undo with nothing to undo");
                    }

                    if (current is { Rolls.Count: > 0 })
                    {
                        undoAfterRoll++;
                    }

                    var (turn, heldThen) = completed[^1];
                    completed.RemoveAt(completed.Count - 1);
                    var box = turn.Box!;
                    cardScores[box.Card][(int)box.Category] = null;
                    undoneBoxes[turn.Turn] = (box.Card, box.Category);
                    turn.Box = null;
                    current = turn;
                    dice = turn.FinalDice;
                    held = heldThen;
                    presses = 0;
                    changes = 0;
                    pressedSinceRoll.Clear();
                    undoCount++;
                    CheckDice(a, dice);
                    if (a.Turn != turn.Turn || a.RollNumber != turn.Rolls.Count)
                    {
                        Fail(a, $"undo restored turn {a.Turn} roll {a.RollNumber}, replay restored turn {turn.Turn} roll {turn.Rolls.Count}");
                    }

                    break;
                }

                default:
                    Fail(a, $"unknown action {a.Action}");
                    break;
            }
        }

        int expectedTurns = Cards * DiceTables.CategoryCount;
        if (completed.Count != expectedTurns)
        {
            throw new ReplayException(gameId, ordered.Count, $"{completed.Count} boxes filled, {expectedTurns} expected for a finished Full Sheet game");
        }

        var finalScores = cardScores.Select(c => c.Select(v => v!.Value).ToArray()).ToArray();
        var totals = finalScores.Select(c => c.Sum() + (c.Take(6).Sum() >= GameCardState.UpperBonusThreshold ? GameCardState.UpperBonusValue : 0)).ToArray();
        if (totals.Sum() != score.Total)
        {
            throw new ReplayException(gameId, ordered.Count, $"replayed total {totals.Sum()} does not match the submitted total {score.Total}");
        }

        return new ReplayedGame
        {
            GameId = gameId,
            UserId = score.UserId,
            Nickname = score.Nickname,
            SubmittedTotal = score.Total,
            PlayedAt = score.PlayedAt,
            Turns = [.. completed.Select(c => c.Turn)],
            CardScores = finalScores,
            CardTotals = totals,
            UndoCount = undoCount,
            UndoChangedBox = undoChanged,
            UndoAfterNextRoll = undoAfterRoll
        };

        ReplayedTurn NewTurn(int number)
        {
            var board = new CardPosition[Cards];
            for (int card = 0; card < Cards; card++)
            {
                int mask = 0;
                int upper = 0;
                for (int c = 0; c < DiceTables.CategoryCount; c++)
                {
                    if (cardScores[card][c] is { } v)
                    {
                        mask |= 1 << c;
                        if (c < 6)
                        {
                            upper += v;
                        }
                    }
                }

                board[card] = new CardPosition(mask, upper);
            }

            return new ReplayedTurn { Turn = number, BoardBefore = board };
        }

        void CheckDice(ActionRow a, int[] expected)
        {
            if (!ParseDice(a).SequenceEqual(expected))
            {
                Fail(a, $"dice {a.Dice} do not match the replayed dice {string.Concat(expected)}");
            }
        }

        [DoesNotReturn]
        void Fail(ActionRow a, string message) => throw new ReplayException(gameId, a.Seq, message);

        int[] ParseDice(ActionRow a)
        {
            if (a.Dice.Length != 5 || a.Dice.Any(ch => ch is < '1' or > '6'))
            {
                Fail(a, $"dice '{a.Dice}' are not five values 1..6");
            }

            return [.. a.Dice.Select(ch => ch - '0')];
        }
    }
}

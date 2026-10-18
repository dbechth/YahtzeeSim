namespace YahtzeeSim.Engine;

public sealed class YahtzeeGameEngine
{
    public const int DiceInGame = 5;
    public const int RollsPerTurn = 3;
    public const int GameCount = 6;

    private readonly Random _random = new();
    private GameEngineSnapshot? _undoSnapshot;

    public List<DieState> Dice { get; private set; } = [];
    public List<GameCardState> Cards { get; private set; } = [];
    public GameMode Mode { get; private set; } = GameMode.Single;
    public int RollCount { get; private set; }

    public bool AwaitingScoreSelection => RollCount >= RollsPerTurn;
    public bool CanRoll => !AwaitingScoreSelection && !IsGameComplete;
    public bool CanUndo => _undoSnapshot != null;
    public bool IsGameComplete => Mode == GameMode.Single
        ? Cards[0].IsComplete
        : Cards.All(c => c.IsComplete);

    public YahtzeeGameEngine()
    {
        Reset(GameMode.Single);
    }

    public void Reset(GameMode mode)
    {
        Mode = mode;
        RollCount = 0;
        _undoSnapshot = null;

        Dice = Enumerable.Range(1, DiceInGame)
            .Select(i => new DieState { Id = i, Value = 0, IsHeld = false })
            .ToList();

        Cards = Enumerable.Range(0, GameCount)
            .Select(_ => new GameCardState())
            .ToList();
    }

    public bool IsCardEnabled(int cardIndex)
    {
        if (Mode == GameMode.Single)
        {
            return cardIndex == 0;
        }

        return cardIndex >= 0 && cardIndex < GameCount;
    }

    public bool ToggleHold(int dieId)
    {
        if (RollCount <= 0 || AwaitingScoreSelection)
        {
            return false;
        }

        var die = Dice.FirstOrDefault(d => d.Id == dieId);
        if (die == null || die.Value <= 0)
        {
            return false;
        }

        die.IsHeld = !die.IsHeld;
        return true;
    }

    public bool CanSelectCategory(int gameIndex, ScoreCategory category)
    {
        if (!IsCardEnabled(gameIndex) || gameIndex < 0 || gameIndex >= GameCount)
        {
            return false;
        }

        if (RollCount <= 0)
        {
            return false;
        }

        var cell = Cards[gameIndex].Scores[category];
        if (cell.IsFilled)
        {
            return false;
        }

        var diceValues = Dice.Select(d => d.Value).ToArray();
        var evaluation = ScoreRules.EvaluateCategory(category, diceValues);

        // Allow ending early only on valid picks; invalid picks become scratch only at roll 3.
        return evaluation.IsValid || AwaitingScoreSelection;
    }

    public bool RollDice()
    {
        if (!CanRoll)
        {
            return false;
        }

        foreach (var die in Dice)
        {
            if (!die.IsHeld)
            {
                die.Value = _random.Next(1, 7);
            }
        }

        RollCount++;

        if (AwaitingScoreSelection)
        {
            foreach (var die in Dice)
            {
                die.IsHeld = true;
            }
        }

        return true;
    }

    public bool TryApplyScore(int gameIndex, ScoreCategory category, string penColorHex, out bool wasScratch, out int storedScore, out bool wasYahtzee)
    {
        wasScratch = false;
        storedScore = 0;
        wasYahtzee = false;

        if (RollCount <= 0)
        {
            return false;
        }

        if (!IsCardEnabled(gameIndex) || gameIndex < 0 || gameIndex >= GameCount)
        {
            return false;
        }

        var card = Cards[gameIndex];
        var cell = card.Scores[category];
        if (cell.IsFilled)
        {
            return false;
        }

        _undoSnapshot = CreateSnapshot();

        var diceValues = Dice.Select(d => d.Value).ToArray();
        var evaluation = ScoreRules.EvaluateCategory(category, diceValues);

        if (evaluation.IsValid)
        {
            cell.Value = evaluation.Score;
            cell.IsScratch = false;
            storedScore = evaluation.Score;
        }
        else if (AwaitingScoreSelection)
        {
            // Forced entry at roll 3: invalid selections are recorded as scratches.
            cell.Value = 0;
            cell.IsScratch = true;
            wasScratch = true;
            storedScore = 0;
        }
        else
        {
            return false;
        }

        cell.PenColorHex = penColorHex;

        wasYahtzee = category == ScoreCategory.Yahtzee && evaluation.IsValid;

        StartNextTurn();
        return true;
    }

    public bool Undo()
    {
        if (_undoSnapshot == null)
        {
            return false;
        }

        RestoreSnapshot(_undoSnapshot);
        _undoSnapshot = null;
        return true;
    }

    public Dictionary<ScoreCategory, double> CalculateProbabilities()
    {
        int[] diceValues = Dice.Select(d => d.Value).ToArray();
        int rollsRemaining = Math.Max(0, RollsPerTurn - RollCount);
        return ProbabilityCalculator.Calculate(diceValues, rollsRemaining);
    }

    public int GetCardTotal(int cardIndex)
    {
        if (cardIndex < 0 || cardIndex >= Cards.Count)
        {
            return 0;
        }

        return Cards[cardIndex].GameTotal ?? 0;
    }

    private void StartNextTurn()
    {
        RollCount = 0;

        foreach (var die in Dice)
        {
            die.Value = 0;
            die.IsHeld = false;
        }
    }

    private GameEngineSnapshot CreateSnapshot()
    {
        return new GameEngineSnapshot
        {
            RollCount = RollCount,
            Mode = Mode,
            Dice = Dice.Select(d => new DieState { Id = d.Id, Value = d.Value, IsHeld = d.IsHeld }).ToList(),
            Cards = Cards.Select(c => c.Clone()).ToList()
        };
    }

    private void RestoreSnapshot(GameEngineSnapshot snapshot)
    {
        RollCount = snapshot.RollCount;
        Mode = snapshot.Mode;
        Dice = snapshot.Dice.Select(d => new DieState { Id = d.Id, Value = d.Value, IsHeld = d.IsHeld }).ToList();
        Cards = snapshot.Cards.Select(c => c.Clone()).ToList();
    }
}

using System.Text.Json;
using System.Text.Json.Serialization;

namespace YahtzeeSim.Engine;

public sealed class YahtzeeGameEngine
{
    public const int DiceInGame = 5;
    public const int RollsPerTurn = 3;
    public const int GameCount = 6;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        Converters = { new JsonStringEnumConverter() }
    };

    private readonly Random _random;
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

    /// <summary>True when the current dice show five of a kind.</summary>
    public bool IsYahtzeeRolled => RollCount > 0 && ScoreRules.IsFiveOfAKind(GetDiceValues());

    /// <summary>
    /// True when the Joker rule applies to the current roll: Single mode, five of a kind,
    /// and the Yahtzee box is already filled (with 50 or a scratch).
    /// </summary>
    public bool IsJokerRoll => IsJokerActive(Cards[0]);

    public YahtzeeGameEngine(Random? random = null)
    {
        _random = random ?? new Random();
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

    /// <summary>Classifies what choosing this cell would do with the current dice.</summary>
    public PickStatus GetPickStatus(int gameIndex, ScoreCategory category)
    {
        if (gameIndex < 0 || gameIndex >= GameCount || !IsCardEnabled(gameIndex) || RollCount <= 0)
        {
            return PickStatus.Unavailable;
        }

        var card = Cards[gameIndex];
        if (card.Scores[category].IsFilled)
        {
            return PickStatus.Unavailable;
        }

        var diceValues = GetDiceValues();

        if (IsJokerActive(card))
        {
            // Joker placement order: matching Upper box, then any open Lower box, then a forced 0 in an Upper box.
            var matchingUpper = ScoreRules.UpperCategories[diceValues[0] - 1];
            if (!card.Scores[matchingUpper].IsFilled)
            {
                return category == matchingUpper ? PickStatus.Valid : PickStatus.Unavailable;
            }

            if (ScoreRules.LowerCategories.Any(c => !card.Scores[c].IsFilled))
            {
                return ScoreRules.LowerCategories.Contains(category) ? PickStatus.Valid : PickStatus.Unavailable;
            }

            return PickStatus.Scratch;
        }

        return ScoreRules.EvaluateCategory(category, diceValues).IsValid ? PickStatus.Valid : PickStatus.Scratch;
    }

    public bool CanSelectCategory(int gameIndex, ScoreCategory category)
    {
        return GetPickStatus(gameIndex, category) != PickStatus.Unavailable;
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

    /// <summary>Scores the current dice in a cell that is valid for them. Does nothing for scratch picks.</summary>
    public ScoreResult TryApplyScore(int gameIndex, ScoreCategory category, string penColorHex)
    {
        return Apply(gameIndex, category, penColorHex, scratch: false);
    }

    /// <summary>Records a cell that is not valid for the current dice as a scratch (X, 0 points).</summary>
    public ScoreResult TryScratch(int gameIndex, ScoreCategory category, string penColorHex)
    {
        return Apply(gameIndex, category, penColorHex, scratch: true);
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
        int[] diceValues = GetDiceValues();
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

    public string Serialize()
    {
        var save = new GameSave { Current = CreateSnapshot(), Undo = _undoSnapshot };
        return JsonSerializer.Serialize(save, JsonOptions);
    }

    /// <summary>Restores a game from <see cref="Serialize"/> output. Returns false and leaves the engine unchanged if the data is unusable.</summary>
    public bool TryLoad(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return false;
        }

        try
        {
            var save = JsonSerializer.Deserialize<GameSave>(json, JsonOptions);
            if (save == null
                || save.Version != GameSave.CurrentVersion
                || !IsValidSnapshot(save.Current)
                || (save.Undo != null && !IsValidSnapshot(save.Undo)))
            {
                return false;
            }

            RestoreSnapshot(save.Current);
            _undoSnapshot = save.Undo;
            return true;
        }
        catch (Exception ex) when (ex is JsonException or NotSupportedException or InvalidOperationException)
        {
            return false;
        }
    }

    private ScoreResult Apply(int gameIndex, ScoreCategory category, string penColorHex, bool scratch)
    {
        var status = GetPickStatus(gameIndex, category);
        if (status != (scratch ? PickStatus.Scratch : PickStatus.Valid))
        {
            return default;
        }

        var card = Cards[gameIndex];
        var cell = card.Scores[category];
        var diceValues = GetDiceValues();
        bool isJoker = IsJokerActive(card);
        var yahtzeeCell = card.Scores[ScoreCategory.Yahtzee];
        bool awardBonus = isJoker && !yahtzeeCell.IsScratch && (yahtzeeCell.Value ?? 0) > 0;

        _undoSnapshot = CreateSnapshot();

        int score = 0;
        if (!scratch)
        {
            score = isJoker
                ? ScoreRules.EvaluateJokerCategory(category, diceValues).Score
                : ScoreRules.EvaluateCategory(category, diceValues).Score;
        }

        cell.Value = score;
        cell.IsScratch = scratch;
        cell.PenColorHex = penColorHex;

        if (awardBonus)
        {
            card.YahtzeeBonusCount++;
        }

        bool wasYahtzee = !scratch && ScoreRules.IsFiveOfAKind(diceValues);

        StartNextTurn();
        return new ScoreResult(true, scratch, score, wasYahtzee, awardBonus);
    }

    private bool IsJokerActive(GameCardState card)
    {
        return Mode == GameMode.Single
            && RollCount > 0
            && ScoreRules.IsFiveOfAKind(GetDiceValues())
            && card.Scores[ScoreCategory.Yahtzee].IsFilled;
    }

    private int[] GetDiceValues()
    {
        return Dice.Select(d => d.Value).ToArray();
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

    private static bool IsValidSnapshot(GameEngineSnapshot? snapshot)
    {
        if (snapshot == null
            || !Enum.IsDefined(snapshot.Mode)
            || snapshot.RollCount < 0
            || snapshot.RollCount > RollsPerTurn
            || snapshot.Dice == null
            || snapshot.Dice.Count != DiceInGame
            || snapshot.Cards == null
            || snapshot.Cards.Count != GameCount)
        {
            return false;
        }

        foreach (var die in snapshot.Dice)
        {
            if (die == null)
            {
                return false;
            }

            bool validValue = snapshot.RollCount > 0 ? die.Value is >= 1 and <= 6 : die.Value == 0;
            if (!validValue)
            {
                return false;
            }
        }

        foreach (var card in snapshot.Cards)
        {
            if (card?.Scores == null || card.YahtzeeBonusCount < 0)
            {
                return false;
            }

            foreach (var category in ScoreRules.ScoringCategories)
            {
                if (!card.Scores.TryGetValue(category, out var cell) || cell == null || cell.Value < 0)
                {
                    return false;
                }
            }
        }

        return true;
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

namespace YahtzeeSim.Engine;

public static class ScoreRules
{
    public static readonly IReadOnlyList<ScoreCategory> UpperCategories =
    [
        ScoreCategory.Aces,
        ScoreCategory.Twos,
        ScoreCategory.Threes,
        ScoreCategory.Fours,
        ScoreCategory.Fives,
        ScoreCategory.Sixes
    ];

    public static readonly IReadOnlyList<ScoreCategory> LowerCategories =
    [
        ScoreCategory.ThreeOfAKind,
        ScoreCategory.FourOfAKind,
        ScoreCategory.FullHouse,
        ScoreCategory.SmallStraight,
        ScoreCategory.LargeStraight,
        ScoreCategory.Yahtzee,
        ScoreCategory.Chance
    ];

    public static readonly IReadOnlyList<ScoreCategory> ScoringCategories =
    [
        ScoreCategory.Aces,
        ScoreCategory.Twos,
        ScoreCategory.Threes,
        ScoreCategory.Fours,
        ScoreCategory.Fives,
        ScoreCategory.Sixes,
        ScoreCategory.ThreeOfAKind,
        ScoreCategory.FourOfAKind,
        ScoreCategory.FullHouse,
        ScoreCategory.SmallStraight,
        ScoreCategory.LargeStraight,
        ScoreCategory.Yahtzee,
        ScoreCategory.Chance
    ];

    public static string GetDisplayName(ScoreCategory category)
    {
        return category switch
        {
            ScoreCategory.Aces => "Aces",
            ScoreCategory.Twos => "Twos",
            ScoreCategory.Threes => "Threes",
            ScoreCategory.Fours => "Fours",
            ScoreCategory.Fives => "Fives",
            ScoreCategory.Sixes => "Sixes",
            ScoreCategory.ThreeOfAKind => "Three of a Kind",
            ScoreCategory.FourOfAKind => "Four of a Kind",
            ScoreCategory.FullHouse => "Full House",
            ScoreCategory.SmallStraight => "Small Straight",
            ScoreCategory.LargeStraight => "Large Straight",
            ScoreCategory.Yahtzee => "Yahtzee",
            ScoreCategory.Chance => "Chance",
            _ => category.ToString()
        };
    }

    public static int GetUpperFaceValue(ScoreCategory category)
    {
        return category switch
        {
            ScoreCategory.Aces => 1,
            ScoreCategory.Twos => 2,
            ScoreCategory.Threes => 3,
            ScoreCategory.Fours => 4,
            ScoreCategory.Fives => 5,
            ScoreCategory.Sixes => 6,
            _ => 0
        };
    }

    public static (bool IsValid, int Score) EvaluateCategory(ScoreCategory category, IReadOnlyList<int> dice)
    {
        var counts = GetCounts(dice);

        if (UpperCategories.Contains(category))
        {
            int face = GetUpperFaceValue(category);
            int count = counts[face];
            return (count > 0, count * face);
        }

        return category switch
        {
            ScoreCategory.ThreeOfAKind => (HasNOfAKind(counts, 3), dice.Sum()),
            ScoreCategory.FourOfAKind => (HasNOfAKind(counts, 4), dice.Sum()),
            ScoreCategory.FullHouse => (IsFullHouse(counts), 25),
            ScoreCategory.SmallStraight => (IsSmallStraight(counts), 30),
            ScoreCategory.LargeStraight => (IsLargeStraight(counts), 40),
            ScoreCategory.Yahtzee => (HasNOfAKind(counts, 5), 50),
            ScoreCategory.Chance => (true, dice.Sum()),
            _ => (false, 0)
        };
    }

    public static bool IsFiveOfAKind(IReadOnlyList<int> dice)
    {
        return dice.Count > 0 && dice[0] >= 1 && dice.All(d => d == dice[0]);
    }

    /// <summary>Scores five of a kind under the Joker rule, where Full House and the straights score in full.</summary>
    public static (bool IsValid, int Score) EvaluateJokerCategory(ScoreCategory category, IReadOnlyList<int> dice)
    {
        return category switch
        {
            ScoreCategory.FullHouse => (true, 25),
            ScoreCategory.SmallStraight => (true, 30),
            ScoreCategory.LargeStraight => (true, 40),
            _ => EvaluateCategory(category, dice)
        };
    }

    public static bool IsCompleteForProbability(ScoreCategory category, IReadOnlyList<int> dice)
    {
        var counts = GetCounts(dice);

        if (UpperCategories.Contains(category))
        {
            int face = GetUpperFaceValue(category);
            return counts[face] >= 3;
        }

        return category switch
        {
            ScoreCategory.ThreeOfAKind => HasNOfAKind(counts, 3),
            ScoreCategory.FourOfAKind => HasNOfAKind(counts, 4),
            ScoreCategory.FullHouse => IsFullHouse(counts),
            ScoreCategory.SmallStraight => IsSmallStraight(counts),
            ScoreCategory.LargeStraight => IsLargeStraight(counts),
            ScoreCategory.Yahtzee => HasNOfAKind(counts, 5),
            ScoreCategory.Chance => true,
            _ => false
        };
    }

    public static int GetUpperPaceDelta(GameCardState card, ScoreCategory category)
    {
        if (!UpperCategories.Contains(category))
        {
            return 0;
        }

        var cell = card.Scores[category];
        if (!cell.IsFilled)
        {
            return 0;
        }

        int face = GetUpperFaceValue(category);
        int observedCount = (cell.Value ?? 0) / face;
        return (observedCount - 3) * face;
    }

    private static int[] GetCounts(IReadOnlyList<int> dice)
    {
        var counts = new int[7];
        foreach (var value in dice)
        {
            if (value >= 1 && value <= 6)
            {
                counts[value]++;
            }
        }

        return counts;
    }

    private static bool HasNOfAKind(IReadOnlyList<int> counts, int n)
    {
        for (int face = 1; face <= 6; face++)
        {
            if (counts[face] >= n)
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsFullHouse(IReadOnlyList<int> counts)
    {
        bool hasThree = false;
        bool hasTwo = false;

        for (int face = 1; face <= 6; face++)
        {
            if (counts[face] == 3)
            {
                hasThree = true;
            }
            else if (counts[face] == 2)
            {
                hasTwo = true;
            }
        }

        return hasThree && hasTwo;
    }

    private static bool IsSmallStraight(IReadOnlyList<int> counts)
    {
        bool[] p = new bool[7];
        for (int face = 1; face <= 6; face++)
        {
            p[face] = counts[face] > 0;
        }

        return (p[1] && p[2] && p[3] && p[4]) ||
               (p[2] && p[3] && p[4] && p[5]) ||
               (p[3] && p[4] && p[5] && p[6]);
    }

    private static bool IsLargeStraight(IReadOnlyList<int> counts)
    {
        bool[] p = new bool[7];
        for (int face = 1; face <= 6; face++)
        {
            p[face] = counts[face] > 0;
        }

        return (p[1] && p[2] && p[3] && p[4] && p[5]) ||
               (p[2] && p[3] && p[4] && p[5] && p[6]);
    }
}

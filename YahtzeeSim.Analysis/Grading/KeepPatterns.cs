namespace YahtzeeSim.Analysis.Grading;

/// <summary>Fixed classification of a set of kept dice. See <see cref="KeepPatterns.Classify"/> for the exact rules.</summary>
public enum KeepPattern
{
    None,
    Single,
    Pair,
    ThreeOfAKind,
    FourOfAKind,
    TwoPair,
    StraightDraw,
    HighPair,
    AllFive,
    Mixed
}

/// <summary>Broad aim of a keep: collecting one number, building a straight, or something else.</summary>
public enum KeepFamily
{
    OfAKind,
    Straight,
    Other
}

public static class KeepPatterns
{
    /// <summary>
    /// Rules, applied in order:
    /// no dice = None; five dice = AllFive; one face only = Single / Pair / ThreeOfAKind / FourOfAKind by count;
    /// three or more different faces, no repeats, spanning at most 5 (fits one large straight) = StraightDraw;
    /// two different faces both 5 or 6 = HighPair; two faces twice each = TwoPair; anything else = Mixed.
    /// </summary>
    public static KeepPattern Classify(IReadOnlyList<int> kept)
    {
        int size = kept.Count;
        if (size == 0)
        {
            return KeepPattern.None;
        }

        if (size == 5)
        {
            return KeepPattern.AllFive;
        }

        var groups = kept.GroupBy(d => d).Select(g => g.Count()).OrderByDescending(c => c).ToList();
        if (groups.Count == 1)
        {
            return size switch
            {
                1 => KeepPattern.Single,
                2 => KeepPattern.Pair,
                3 => KeepPattern.ThreeOfAKind,
                _ => KeepPattern.FourOfAKind
            };
        }

        if (groups.Count == size && size >= 3 && kept.Max() - kept.Min() <= 4)
        {
            return KeepPattern.StraightDraw;
        }

        if (size == 2 && kept.All(d => d >= 5))
        {
            return KeepPattern.HighPair;
        }

        if (groups.Count == 2 && groups[0] == 2 && groups[1] == 2)
        {
            return KeepPattern.TwoPair;
        }

        return KeepPattern.Mixed;
    }

    public static KeepFamily FamilyOf(KeepPattern pattern) => pattern switch
    {
        KeepPattern.Pair or KeepPattern.ThreeOfAKind or KeepPattern.FourOfAKind => KeepFamily.OfAKind,
        KeepPattern.StraightDraw => KeepFamily.Straight,
        _ => KeepFamily.Other
    };

    public static string DisplayName(KeepPattern pattern) => pattern switch
    {
        KeepPattern.None => "Reroll all",
        KeepPattern.Single => "One die",
        KeepPattern.Pair => "Pair",
        KeepPattern.ThreeOfAKind => "Three of a kind",
        KeepPattern.FourOfAKind => "Four of a kind",
        KeepPattern.TwoPair => "Two pair",
        KeepPattern.StraightDraw => "Straight draw",
        KeepPattern.HighPair => "5 and 6",
        KeepPattern.AllFive => "All five",
        _ => "Mixed"
    };
}

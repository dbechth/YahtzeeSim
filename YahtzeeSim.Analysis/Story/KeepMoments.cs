using YahtzeeSim.Analysis.Replay;

namespace YahtzeeSim.Analysis.Story;

/// <summary>What a roll offers before the player decides what to keep. See <see cref="KeepMoments.ClassifyRoll"/>.</summary>
public enum RollSituation
{
    Yahtzee,
    FourOfAKind,
    FullHouse,
    LargeStraight,
    FourInARow,
    ThreeOfAKind,
    TwoPair,
    PairAndRun,
    Pair,
    ThreeInARow,
    Nothing
}

/// <summary>What the player did with that roll. See <see cref="KeepMoments.ClassifyResponse"/>.</summary>
public enum KeepResponse
{
    /// <summary>Scored without rolling again.</summary>
    Stopped,

    /// <summary>Kept two or more dice of one number.</summary>
    KeptMatching,

    /// <summary>Kept three or more different numbers that fit one large straight.</summary>
    KeptStraight,

    /// <summary>Kept a single die.</summary>
    KeptOneDie,

    /// <summary>Rolled all five again.</summary>
    RerolledAll,

    /// <summary>Any other keep.</summary>
    KeptMixed
}

/// <summary>One decision after roll 1 or roll 2: the dice showing and what the player kept.</summary>
public sealed record KeepMoment(string GameId, int Turn, int RollNumber, int[] Dice, int[] Kept, RollSituation Situation, KeepResponse Response)
{
    /// <summary>True when the roll offered both matching dice and a straight draw, so the choice shows a preference.</summary>
    public bool OffersBoth => Situation is RollSituation.FourInARow or RollSituation.PairAndRun
        || (Situation == RollSituation.TwoPair && KeepMoments.LongestRun(Dice) >= 3);
}

/// <summary>Fixed, rule-based classification of rolls and keeps. No judgement: it only counts what the dice and holds were.</summary>
public static class KeepMoments
{
    /// <summary>Every keep or stop decision after roll 1 and roll 2 in the games, in game and turn order.</summary>
    public static List<KeepMoment> From(IEnumerable<ReplayedGame> games)
    {
        var moments = new List<KeepMoment>();
        foreach (var game in games)
        {
            foreach (var turn in game.Turns)
            {
                for (int i = 0; i < turn.Rolls.Count && turn.Rolls[i].RollNumber < 3; i++)
                {
                    var roll = turn.Rolls[i];
                    int[] kept = i + 1 < turn.Rolls.Count ? turn.Rolls[i + 1].KeptValues(roll.Dice) : roll.Dice;
                    moments.Add(new KeepMoment(game.GameId, turn.Turn, roll.RollNumber, roll.Dice, kept, ClassifyRoll(roll.Dice), ClassifyResponse(kept)));
                }
            }
        }

        return moments;
    }

    /// <summary>
    /// First match wins: five of a kind; four of a kind; full house (three and two); large straight; four in a row;
    /// three of a kind; two pair; a pair with three in a row; a pair; three in a row; nothing.
    /// </summary>
    public static RollSituation ClassifyRoll(IReadOnlyList<int> dice)
    {
        var counts = dice.GroupBy(d => d).Select(g => g.Count()).OrderByDescending(c => c).ToList();
        int run = LongestRun(dice);
        int second = counts.Count > 1 ? counts[1] : 0;
        return counts[0] switch
        {
            5 => RollSituation.Yahtzee,
            4 => RollSituation.FourOfAKind,
            3 when second == 2 => RollSituation.FullHouse,
            _ when run == 5 => RollSituation.LargeStraight,
            _ when run == 4 => RollSituation.FourInARow,
            3 => RollSituation.ThreeOfAKind,
            2 when second == 2 => RollSituation.TwoPair,
            2 when run >= 3 => RollSituation.PairAndRun,
            2 => RollSituation.Pair,
            _ when run >= 3 => RollSituation.ThreeInARow,
            _ => RollSituation.Nothing
        };
    }

    /// <summary>
    /// Five dice kept = stopped; none = rerolled all; one = one die; all the same number = matching;
    /// all different and spanning at most five numbers (three or more dice) = straight; anything else = mixed.
    /// </summary>
    public static KeepResponse ClassifyResponse(IReadOnlyList<int> kept)
    {
        if (kept.Count == 5)
        {
            return KeepResponse.Stopped;
        }

        if (kept.Count == 0)
        {
            return KeepResponse.RerolledAll;
        }

        if (kept.Count == 1)
        {
            return KeepResponse.KeptOneDie;
        }

        int distinct = kept.Distinct().Count();
        if (distinct == 1)
        {
            return KeepResponse.KeptMatching;
        }

        return distinct == kept.Count && kept.Count >= 3 && kept.Max() - kept.Min() <= 4
            ? KeepResponse.KeptStraight
            : KeepResponse.KeptMixed;
    }

    /// <summary>Length of the longest run of consecutive numbers showing (1 to 5).</summary>
    public static int LongestRun(IReadOnlyList<int> dice)
    {
        int best = 0;
        int current = 0;
        for (int face = 1; face <= 6; face++)
        {
            current = dice.Contains(face) ? current + 1 : 0;
            best = Math.Max(best, current);
        }

        return best;
    }
}

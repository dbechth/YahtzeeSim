using System.Globalization;
using YahtzeeSim.Analysis.Grading;
using YahtzeeSim.Analysis.Rules;
using YahtzeeSim.Engine;

namespace YahtzeeSim.Analysis.Story;

/// <summary>"When you roll …" line: the situation, the sentence, and one example roll with the dice that were kept.</summary>
public sealed record RollLine(RollSituation Situation, string Lead, string Text, int[] ExampleDice, int[] ExampleKept, int Count, int Total);

/// <summary>The gentle tip: a friendly sentence for the player's costliest habit and one example from their games.</summary>
public sealed record StoryTip(string RuleId, string Text, string? Example, string? ExampleDice);

/// <summary>Everything the player's story says. Every sentence comes from a template in <see cref="StoryRules"/>.</summary>
public sealed record PlayStory(
    string Nickname,
    int Games,
    Badge Badge,
    string BadgeLine,
    IReadOnlyList<RollLine> RollLines,
    IReadOnlyList<string> SheetLines,
    IReadOnlyList<string> Highlights,
    StoryTip? Tip);

/// <summary>
/// The versioned story templates and thresholds. Plain descriptions of the player's own games, plus one tip from the
/// fired habit rule with the most points lost. Change <see cref="Version"/> whenever a template or threshold changes.
/// </summary>
public static class StoryRules
{
    public const string Version = "1";

    /// <summary>A "When you roll" line needs a situation seen at least this many times on opening rolls.</summary>
    public const int MinSituationCount = 4;

    public const int MaxRollLines = 4;

    /// <summary>Rules that describe a habit and have a friendly tip. Other rules (overall cost, outcomes) never become tips.</summary>
    public static IReadOnlyDictionary<string, string> Tips { get; } = new Dictionary<string, string>
    {
        ["UPPER-BELOW-PAR"] = "Try saving the top boxes for three or more of a number. A low score there early makes the 35-point bonus harder to reach.",
        ["YAHTZEE-SCRATCHED-EARLY"] = "Try keeping your Yahtzee boxes open longer. With plenty of turns left there is still a good chance to fill them.",
        ["SCRATCH-AVOIDABLE"] = "Before crossing out a box, look for an open box on any card where the dice still score.",
        ["CHANCE-LOW"] = "Try saving Chance for a high total. It is your safety net for a big roll that fits nowhere else.",
        ["STOPPED-EARLY"] = "Try using your remaining rolls more often. Rolling again usually pays off more than stopping early.",
        ["KIND-OVER-STRAIGHT"] = "When a straight is already started, try chasing it instead of keeping a pair.",
        ["STRAIGHT-OVER-KIND"] = "When you have matching dice, try keeping them instead of chasing a straight.",
        ["KEPT-TOO-FEW"] = "Try keeping more of your useful dice instead of rolling them again.",
        ["KEPT-TOO-MANY"] = "Try rolling the dice that don't help your plan instead of keeping them.",
        ["SAME-BOX-OTHER-CARD"] = "When the same box is open on several cards, think about which card needs it most, such as the one closest to its upper bonus.",
    };

    public const string NoTip = "Nothing stands out to work on right now. Keep rolling!";

    public static PlayStory Build(PlayerAnalysis player)
    {
        var replayed = player.Games.Select(g => g.Game).ToList();
        var metrics = StyleMetrics.From(replayed);
        var badge = BadgeRules.Choose(metrics).Badge;
        var moments = KeepMoments.From(replayed);
        return new PlayStory(
            player.Nickname,
            player.Games.Count,
            badge,
            BadgeLine(badge, metrics),
            RollLines(moments),
            SheetLines(player, metrics),
            Highlights(player, metrics),
            Tip(player.Findings));
    }

    public static string BadgeLine(Badge badge, StyleMetrics m) => badge.Id switch
    {
        "yahtzee-hunter" => $"When a roll offered both a set and a straight, you kept the matching dice {I(m.KeptMatching)} of {I(m.ChoiceRolls)} times, where a strong player would about {I((int)Math.Round(m.ReferenceMatching))}.",
        "straight-shooter" => $"When a roll offered both a set and a straight, you went for the straight {I(m.KeptStraight)} of {I(m.ChoiceRolls)} times, where a strong player would about {I((int)Math.Round(m.ReferenceStraight))}.",
        "bonus-banker" => m.UpperBonuses == m.Cards
            ? $"You earned the upper bonus on every one of your {I(m.Cards)} cards."
            : $"You earned the upper bonus on {I(m.UpperBonuses)} of your {I(m.Cards)} cards.",
        "card-closer" => $"You usually have only {CardsText(m.CardsInProgress)} on the go at once.",
        "card-juggler" => $"You keep about {CardsText(m.CardsInProgress)} going at once.",
        "sure-thing" => $"You scored before the third roll on {I(m.EarlyStops)} of {I(m.Turns)} turns.",
        "quick-draw" => $"Your typical turn takes just {Seconds(m.MedianTurnSeconds)}.",
        "deep-thinker" => $"Your typical turn takes about {Seconds(m.MedianTurnSeconds)}.",
        _ => "No single habit stands out. You mix up your choices to suit the dice."
    };

    /// <summary>
    /// One line per opening-roll situation seen at least <see cref="MinSituationCount"/> times (five of a kind excluded),
    /// most frequent first, at most <see cref="MaxRollLines"/>. The most common response sets the wording:
    /// 80% or more "almost always", 60% "usually", 50% "more often than not"; below that the line names the two most
    /// common responses.
    /// </summary>
    public static List<RollLine> RollLines(IEnumerable<KeepMoment> moments)
    {
        var lines = new List<RollLine>();
        var groups = moments
            .Where(m => m.RollNumber == 1 && m.Situation != RollSituation.Yahtzee)
            .GroupBy(m => m.Situation)
            .Where(g => g.Count() >= MinSituationCount)
            .OrderByDescending(g => g.Count())
            .ThenBy(g => g.Key)
            .Take(MaxRollLines);

        foreach (var group in groups)
        {
            int total = group.Count();
            var responses = group.GroupBy(m => m.Response)
                .Select(r => (Response: r.Key, Count: r.Count(), First: r.First()))
                .OrderByDescending(r => r.Count)
                .ThenBy(r => r.Response)
                .ToList();
            var top = responses[0];
            double share = (double)top.Count / total;
            string text;
            if (share >= 0.5)
            {
                string often = share >= 0.8 ? "almost always" : share >= 0.6 ? "usually" : "more often than not";
                text = $"you {often} {Phrase(group.Key, top.Response)}: {I(top.Count)} of {I(total)} times.";
            }
            else
            {
                var next = responses[1];
                text = $"you mix it up. Most often you {Phrase(group.Key, top.Response)} ({I(top.Count)} of {I(total)}), otherwise you {Phrase(group.Key, next.Response)} ({I(next.Count)}).";
            }

            lines.Add(new RollLine(group.Key, Lead(group.Key), text, top.First.Dice, top.First.Kept, top.Count, total));
        }

        return lines;
    }

    public static List<string> SheetLines(PlayerAnalysis player, StyleMetrics m)
    {
        var lines = new List<string>();
        lines.Add(m.CardsInProgress < 2.5
            ? "You like to finish one card before you start the next."
            : m.CardsInProgress < 4
                ? $"You usually have {CardsText(m.CardsInProgress)} on the go."
                : $"You keep about {CardsText(m.CardsInProgress)} going at once.");

        double threeRolls = m.Turns == 0 ? 0 : 1 - m.EarlyStopRate;
        lines.Add(threeRolls >= 0.85
            ? "You almost always use all three rolls."
            : m.EarlyStopRate >= 0.2
                ? $"You often take your points before the third roll ({I(m.EarlyStops)} of {I(m.Turns)} turns)."
                : "You use all three rolls on most turns.");

        lines.Add(m.UpperBonuses == m.Cards
            ? $"You earned the upper bonus on all {I(m.Cards)} cards."
            : $"You earned the upper bonus on {I(m.UpperBonuses)} of {I(m.Cards)} cards.");

        var categories = player.Profile.Categories.Where(c => c.Fills > 0).ToList();
        var chance = categories.FirstOrDefault(c => c.Category == ScoreCategory.Chance);
        if (chance != null && chance.AverageTurn >= 52)
        {
            lines.Add("You save Chance for late in the game.");
        }
        else if (chance != null && chance.AverageTurn <= 26)
        {
            lines.Add("You tend to use Chance early.");
        }

        if (categories.Count >= 2)
        {
            var first = categories.OrderBy(c => c.AverageTurn).ThenBy(c => c.Category).First();
            var last = categories.OrderByDescending(c => c.AverageTurn).ThenBy(c => c.Category).First();
            lines.Add($"{ScoreRules.GetDisplayName(first.Category)} tends to fill first and {ScoreRules.GetDisplayName(last.Category)} last.");
        }

        return lines;
    }

    public static List<string> Highlights(PlayerAnalysis player, StyleMetrics m)
    {
        var lines = new List<string>();
        int best = player.Games.Max(g => g.Game.Total);
        lines.Add(player.Games.Count == 1
            ? $"Your game scored {I(best)}."
            : $"Best game {I(best)}, average {I((int)Math.Round(player.AverageTotal))} over {I(player.Games.Count)} games.");

        if (m.YahtzeesScored > 0)
        {
            lines.Add(m.YahtzeesScored == 1
                ? "You scored a Yahtzee."
                : $"You scored {I(m.YahtzeesScored)} Yahtzees{(player.Games.Count > 1 ? $" in {I(player.Games.Count)} games" : string.Empty)}.");
        }

        var sums = new[] { ScoreCategory.ThreeOfAKind, ScoreCategory.FourOfAKind, ScoreCategory.Chance };
        var biggest = player.Games
            .SelectMany(g => g.Game.Turns)
            .Where(t => sums.Contains(t.Box!.Category) && !t.Box.Scratch)
            .OrderByDescending(t => t.Box!.Points)
            .ThenBy(t => t.Turn)
            .FirstOrDefault();
        if (biggest != null)
        {
            lines.Add($"Your biggest roll: {I(biggest.Box!.Points)} in {ScoreRules.GetDisplayName(biggest.Box.Category)}.");
        }

        return lines;
    }

    /// <summary>The fired rule with a tip and the most points lost (ties by rule id). Null-free: falls back to <see cref="NoTip"/>.</summary>
    public static StoryTip Tip(IReadOnlyList<RuleFinding> findings)
    {
        var finding = findings
            .Where(f => f.Fired && Tips.ContainsKey(f.Rule.Id))
            .OrderByDescending(f => f.Match.PointsLost)
            .ThenBy(f => f.Rule.Id, StringComparer.Ordinal)
            .FirstOrDefault();
        if (finding == null)
        {
            return new StoryTip(string.Empty, NoTip, null, null);
        }

        var example = finding.Match.Examples.FirstOrDefault();
        return new StoryTip(
            finding.Rule.Id,
            Tips[finding.Rule.Id],
            example == null ? null : ExampleText(example),
            example?.Dice);
    }

    public static string ExampleText(GradedDecision d)
    {
        string chosen = d.Kind == DecisionKind.Box ? BoxText(d.ChosenBox!, past: true) : KeepText(d.Chosen, past: true);
        string best = d.Kind == DecisionKind.Box ? BoxText(d.BestBox!, past: false) : KeepText(d.Best, past: false);
        return $"On turn {I(d.Turn)} you {chosen}; {best} was the stronger play by about {d.Cost.ToString("0", CultureInfo.InvariantCulture)} points on average.";
    }

    private static string BoxText(BoxChoice box, bool past)
    {
        string name = ScoreRules.GetDisplayName(box.Category);
        if (past)
        {
            return box.Scratch ? $"crossed out {name} on Game {I(box.Card + 1)}" : $"scored {I(box.Points)} in {name} on Game {I(box.Card + 1)}";
        }

        return box.Scratch ? $"crossing out {name} on Game {I(box.Card + 1)}" : $"{I(box.Points)} in {name} on Game {I(box.Card + 1)}";
    }

    private static string KeepText(string text, bool past) => text switch
    {
        "Score now" => past ? "scored right away" : "rolling again",
        "Reroll all" => past ? "rolled all five again" : "rolling all five again",
        _ => (past ? "kept " : "keeping ") + string.Join(" ", text["Keep ".Length..].ToCharArray())
    };

    private static string Lead(RollSituation situation) => situation switch
    {
        RollSituation.FourOfAKind => "Four of a kind",
        RollSituation.FullHouse => "A full house",
        RollSituation.LargeStraight => "A large straight",
        RollSituation.FourInARow => "Four in a row",
        RollSituation.ThreeOfAKind => "Three of a kind",
        RollSituation.TwoPair => "Two pairs",
        RollSituation.PairAndRun => "A pair and three in a row",
        RollSituation.Pair => "A pair",
        RollSituation.ThreeInARow => "Three in a row",
        RollSituation.Nothing => "Nothing matching",
        _ => "Five of a kind"
    };

    private static string Phrase(RollSituation situation, KeepResponse response) => response switch
    {
        KeepResponse.Stopped => "score it straight away",
        KeepResponse.KeptStraight => "go for the straight",
        KeepResponse.KeptOneDie => "keep a single die and roll the rest",
        KeepResponse.RerolledAll => "roll all five again",
        KeepResponse.KeptMixed => "keep a mix of dice",
        _ => situation switch
        {
            RollSituation.TwoPair => "keep one pair and roll for more",
            RollSituation.ThreeOfAKind => "keep the three and roll for more",
            RollSituation.FourOfAKind => "keep the four and go for a Yahtzee",
            RollSituation.FullHouse => "break it up and keep the three",
            RollSituation.FourInARow => "keep a pair instead",
            _ => "keep the pair"
        }
    };

    private static string CardsText(double cards)
    {
        int n = Math.Max(1, (int)Math.Round(cards, MidpointRounding.AwayFromZero));
        return n == 1 ? "one card" : $"{I(n)} cards";
    }

    private static string Seconds(double s) => s.ToString("0", CultureInfo.InvariantCulture) + " seconds";

    private static string I(int v) => v.ToString(CultureInfo.InvariantCulture);
}

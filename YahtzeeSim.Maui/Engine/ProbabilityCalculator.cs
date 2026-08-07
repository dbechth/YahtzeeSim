namespace YahtzeeSim.Maui.Engine;

internal static class ProbabilityCalculator
{
    private const int DiceCount = 5;
    private const int UpperSectionThreshold = 3;

    /// <summary>
    /// Calculates the probability of completing each scoring category given the
    /// current dice values and the number of rolls remaining in the turn.
    /// </summary>
    public static Dictionary<ScoreCategory, double> Calculate(int[] diceValues, int rollsRemaining)
    {
        var result = ScoreRules.ScoringCategories.ToDictionary(c => c, _ => 0d);

        rollsRemaining = Math.Max(0, rollsRemaining);

        var startingDice = diceValues
            .Take(DiceCount)
            .Select(v => Math.Clamp(v, 1, 6))
            .ToArray();

        foreach (var category in ScoreRules.ScoringCategories)
        {
            result[category] = EstimateProbability(category, startingDice, rollsRemaining);
        }

        return result;
    }

    private static double EstimateProbability(ScoreCategory category, int[] startingDice, int rollsRemaining)
    {
        // If no rolls are left, the result is already determined.
        if (rollsRemaining == 0)
        {
            return ScoreRules.IsCompleteForProbability(category, startingDice) ? 1d : 0d;
        }

        // If the starting dice already satisfy the category, every trial would succeed — short-circuit.
        if (ScoreRules.IsCompleteForProbability(category, startingDice))
        {
            return 1d;
        }

        // Upper section categories use an exact Markov chain solution.
        // This avoids Monte Carlo noise that would make symmetric categories
        // (e.g. Twos and Threes with no matching dice) show different probabilities.
        int upperFace = ScoreRules.GetUpperFaceValue(category);
        if (upperFace > 0)
        {
            return UpperSectionExactProbability(startingDice, upperFace, rollsRemaining, UpperSectionThreshold);
        }

        // Lower section categories use Monte Carlo simulation.
        return MonteCarloEstimate(category, startingDice, rollsRemaining);
    }

    // Computes the exact probability of accumulating at least `threshold` dice showing
    // `targetFace` within `rolls` rolls, using a Markov chain over the number of
    // matching dice held. Holding matching dice between rolls is assumed.
    private static double UpperSectionExactProbability(int[] dice, int targetFace, int rolls, int threshold)
    {
        int initial = dice.Count(d => d == targetFace);
        if (initial >= threshold) return 1d;
        if (rolls == 0) return 0d;

        // prob[c] = probability of currently holding exactly c matching dice
        var prob = new double[DiceCount + 1];
        prob[initial] = 1d;

        for (int roll = 0; roll < rolls; roll++)
        {
            var next = new double[DiceCount + 1];

            for (int c = 0; c < threshold; c++)
            {
                if (prob[c] == 0d) continue;

                int free = DiceCount - c; // dice being rerolled this turn

                // New matches follow Binomial(free, 1/6)
                for (int j = 0; j <= free; j++)
                {
                    int newC = Math.Min(c + j, DiceCount);
                    double p = BinomCoeff(free, j)
                        * Math.Pow(1.0 / 6.0, j)
                        * Math.Pow(5.0 / 6.0, free - j);
                    next[newC] += prob[c] * p;
                }
            }

            // Absorbing states (already at or above threshold) carry forward unchanged
            for (int c = threshold; c <= DiceCount; c++)
                next[c] += prob[c];

            prob = next;
        }

        double success = 0d;
        for (int c = threshold; c <= DiceCount; c++)
            success += prob[c];
        return success;
    }

    // Estimates probability via Monte Carlo simulation, using a greedy hold strategy
    // optimized per category for each roll.
    private static double MonteCarloEstimate(ScoreCategory category, int[] startingDice, int rollsRemaining)
    {
        const int trials = 4000;
        int successes = 0;

        for (int trial = 0; trial < trials; trial++)
        {
            var simDice = (int[])startingDice.Clone();

            // Simulate each remaining roll, using the hold strategy to keep beneficial dice.
            for (int roll = 0; roll < rollsRemaining; roll++)
            {
                // Stop rolling early if the category is already complete.
                if (ScoreRules.IsCompleteForProbability(category, simDice))
                    break;

                // Determine which dice to keep, then reroll the rest randomly.
                var hold = BuildHoldMask(category, simDice);
                for (int i = 0; i < simDice.Length; i++)
                {
                    if (!hold[i])
                        simDice[i] = Random.Shared.Next(1, 7);
                }
            }

            if (ScoreRules.IsCompleteForProbability(category, simDice))
                successes++;
        }

        return (double)successes / trials;
    }

    // Returns a hold mask indicating which dice to keep for the given category,
    // using a greedy strategy aimed at completing it as efficiently as possible.
    private static bool[] BuildHoldMask(ScoreCategory category, int[] dice)
    {
        var hold = new bool[dice.Length];

        // Chance: all dice count, never reroll anything.
        if (category == ScoreCategory.Chance)
        {
            Array.Fill(hold, true);
            return hold;
        }

        // Upper section: hold dice that match the target face.
        int targetFace = ScoreRules.GetUpperFaceValue(category);
        if (targetFace > 0)
        {
            for (int i = 0; i < dice.Length; i++)
                hold[i] = dice[i] == targetFace;
            return hold;
        }

        var counts = GetCounts(dice);

        // Three of a Kind / Four of a Kind / Yahtzee: hold the most frequent face.
        if (category is ScoreCategory.ThreeOfAKind or ScoreCategory.FourOfAKind or ScoreCategory.Yahtzee)
        {
            int bestFace = 1;
            for (int face = 2; face <= 6; face++)
            {
                if (counts[face] > counts[bestFace])
                    bestFace = face;
            }
            for (int i = 0; i < dice.Length; i++)
                hold[i] = dice[i] == bestFace;
            return hold;
        }

        // Full House: hold at most 3 of the most frequent face and at most 2 of the second.
        // Capping prevents getting stuck when holding e.g. four-of-a-kind + one other,
        // where nothing would be rerolled and the full house could never complete.
        if (category == ScoreCategory.FullHouse)
        {
            int firstFace = 1, secondFace = 2;
            for (int face = 1; face <= 6; face++)
            {
                if (counts[face] > counts[firstFace]) { secondFace = firstFace; firstFace = face; }
                else if (face != firstFace && counts[face] > counts[secondFace]) secondFace = face;
            }

            int firstHeld = 0, secondHeld = 0;
            for (int i = 0; i < dice.Length; i++)
            {
                if (dice[i] == firstFace && firstHeld < 3) { hold[i] = true; firstHeld++; }
                else if (dice[i] == secondFace && secondHeld < 2) { hold[i] = true; secondHeld++; }
            }
            return hold;
        }

        // Small / Large Straight: find the candidate run with the most overlap,
        // then hold exactly one die per required value (duplicates are rerolled).
        if (category is ScoreCategory.SmallStraight or ScoreCategory.LargeStraight)
        {
            int[][] runs = category == ScoreCategory.SmallStraight
                ? [[1, 2, 3, 4], [2, 3, 4, 5], [3, 4, 5, 6]]
                : [[1, 2, 3, 4, 5], [2, 3, 4, 5, 6]];

            int[] bestRun = runs[0];
            int bestCount = runs[0].Count(v => dice.Contains(v));
            foreach (var run in runs.Skip(1))
            {
                int count = run.Count(v => dice.Contains(v));
                if (count > bestCount) { bestCount = count; bestRun = run; }
            }

            var used = new HashSet<int>();
            for (int i = 0; i < dice.Length; i++)
            {
                if (bestRun.Contains(dice[i]) && used.Add(dice[i]))
                    hold[i] = true;
            }
            return hold;
        }

        return hold;
    }

    private static int[] GetCounts(int[] dice)
    {
        var counts = new int[7];
        foreach (var v in dice)
            if (v >= 1 && v <= 6) counts[v]++;
        return counts;
    }

    private static double BinomCoeff(int n, int k)
    {
        if (k < 0 || k > n) return 0d;
        if (k > n - k) k = n - k;
        double result = 1d;
        for (int i = 0; i < k; i++) { result *= (n - i); result /= (i + 1); }
        return result;
    }
}

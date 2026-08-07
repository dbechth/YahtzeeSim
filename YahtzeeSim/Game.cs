using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace YahtzeeSim
{
    internal class Game
    {
        const int diceInGame = 5;
        const int rollsPerTurn = 3;
        const int turnsPerGame = 78;
        public List<Dice> GameDice = new List<Dice>();
        public int RollCount { get; set; }
        public int TurnCount { get; set; }
        public bool CanUndo => _lastSnapshot != null;

        private sealed class GameSnapshot
        {
            public int RollCount { get; set; }
            public int TurnCount { get; set; }
            public int[] DiceValues { get; set; } = Array.Empty<int>();
            public bool[] Holds { get; set; } = Array.Empty<bool>();
        }

        private GameSnapshot? _lastSnapshot;

        public Game()
        {
            NewGame();
        }

        public void NewGame()
        {
            GameDice = new List<Dice>();
            for (int i = 1; i < diceInGame +1; i++)
            {
                GameDice.Add(new Dice() { ID = i });
            }
            RollCount = 0;
            TurnCount = 0;
            _lastSnapshot = null;
        }

        public Dictionary<string, double> CalculateProbabilities(List<Dice> diceValues, int rollsRemaining)
        {
            var categories = new[]
            {
                "Aces", "Twos", "Threes", "Fours", "Fives", "Sixes",
                "Three of a Kind", "Four of a Kind", "Full House",
                "Small Straight", "Large Straight", "Yahtzee", "Chance"
            };

            var result = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
            foreach (var category in categories)
            {
                result[category] = 0d;
            }

            if (diceValues == null || diceValues.Count == 0)
            {
                result["Chance"] = 1d;
                return result;
            }

            rollsRemaining = Math.Max(0, rollsRemaining);
            var startingDice = diceValues
                .Take(diceInGame)
                .Select(d => Math.Clamp(d.Value, 1, 6))
                .ToArray();

            if (startingDice.Length < diceInGame)
            {
                var padded = new int[diceInGame];
                for (int i = 0; i < diceInGame; i++)
                {
                    padded[i] = i < startingDice.Length ? startingDice[i] : 1;
                }
                startingDice = padded;
            }

            static int[] GetCounts(int[] dice)
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

            static bool HasNOfAKind(int[] dice, int n)
            {
                var counts = GetCounts(dice);
                for (int i = 1; i <= 6; i++)
                {
                    if (counts[i] >= n)
                    {
                        return true;
                    }
                }
                return false;
            }

            static bool IsFullHouse(int[] dice)
            {
                var counts = GetCounts(dice);
                bool hasThree = false;
                bool hasTwo = false;

                for (int i = 1; i <= 6; i++)
                {
                    if (counts[i] == 3)
                    {
                        hasThree = true;
                    }
                    else if (counts[i] == 2)
                    {
                        hasTwo = true;
                    }
                }

                return hasThree && hasTwo;
            }

            static bool IsSmallStraight(int[] dice)
            {
                bool[] present = new bool[7];
                foreach (var value in dice)
                {
                    if (value >= 1 && value <= 6)
                    {
                        present[value] = true;
                    }
                }

                return (present[1] && present[2] && present[3] && present[4]) ||
                       (present[2] && present[3] && present[4] && present[5]) ||
                       (present[3] && present[4] && present[5] && present[6]);
            }

            static bool IsLargeStraight(int[] dice)
            {
                bool[] present = new bool[7];
                foreach (var value in dice)
                {
                    if (value >= 1 && value <= 6)
                    {
                        present[value] = true;
                    }
                }

                return (present[1] && present[2] && present[3] && present[4] && present[5]) ||
                       (present[2] && present[3] && present[4] && present[5] && present[6]);
            }

            static bool IsCategoryComplete(string category, int[] dice)
            {
                var counts = GetCounts(dice);

                return category switch
                {
                    "Aces" => counts[1] >= 3,
                    "Twos" => counts[2] >= 3,
                    "Threes" => counts[3] >= 3,
                    "Fours" => counts[4] >= 3,
                    "Fives" => counts[5] >= 3,
                    "Sixes" => counts[6] >= 3,
                    "Three of a Kind" => HasNOfAKind(dice, 3),
                    "Four of a Kind" => HasNOfAKind(dice, 4),
                    "Full House" => IsFullHouse(dice),
                    "Small Straight" => IsSmallStraight(dice),
                    "Large Straight" => IsLargeStraight(dice),
                    "Yahtzee" => HasNOfAKind(dice, 5),
                    "Chance" => true,
                    _ => false
                };
            }

            static bool[] BuildHoldMask(string category, int[] dice)
            {
                var hold = new bool[dice.Length];

                if (category == "Chance")
                {
                    for (int i = 0; i < hold.Length; i++)
                    {
                        hold[i] = true;
                    }
                    return hold;
                }

                int targetFace = category switch
                {
                    "Aces" => 1,
                    "Twos" => 2,
                    "Threes" => 3,
                    "Fours" => 4,
                    "Fives" => 5,
                    "Sixes" => 6,
                    _ => 0
                };

                if (targetFace > 0)
                {
                    for (int i = 0; i < dice.Length; i++)
                    {
                        hold[i] = dice[i] == targetFace;
                    }
                    return hold;
                }

                if (category == "Three of a Kind" || category == "Four of a Kind" || category == "Yahtzee")
                {
                    var counts = GetCounts(dice);
                    int bestFace = 1;
                    for (int face = 2; face <= 6; face++)
                    {
                        if (counts[face] > counts[bestFace])
                        {
                            bestFace = face;
                        }
                    }

                    for (int i = 0; i < dice.Length; i++)
                    {
                        hold[i] = dice[i] == bestFace;
                    }
                    return hold;
                }

                if (category == "Full House")
                {
                    var counts = GetCounts(dice);

                    // Find the two most frequent faces. firstFace becomes the "three", secondFace the "two".
                    int firstFace = 1;
                    int secondFace = 2;

                    for (int face = 1; face <= 6; face++)
                    {
                        if (counts[face] > counts[firstFace])
                        {
                            secondFace = firstFace;
                            firstFace = face;
                        }
                        else if (face != firstFace && counts[face] > counts[secondFace])
                        {
                            secondFace = face;
                        }
                    }

                    // Hold at most 3 of firstFace and at most 2 of secondFace.
                    // This prevents getting stuck when holding e.g. four-of-a-kind + one other,
                    // where nothing would be rerolled and the full house could never complete.
                    int firstHeld = 0;
                    int secondHeld = 0;
                    for (int i = 0; i < dice.Length; i++)
                    {
                        if (dice[i] == firstFace && firstHeld < 3)
                        {
                            hold[i] = true;
                            firstHeld++;
                        }
                        else if (dice[i] == secondFace && secondHeld < 2)
                        {
                            hold[i] = true;
                            secondHeld++;
                        }
                    }
                    return hold;
                }

                if (category == "Small Straight" || category == "Large Straight")
                {
                    var runA = category == "Small Straight" ? new[] { 1, 2, 3, 4 } : new[] { 1, 2, 3, 4, 5 };
                    var runB = category == "Small Straight" ? new[] { 2, 3, 4, 5 } : new[] { 2, 3, 4, 5, 6 };
                    var runC = category == "Small Straight" ? new[] { 3, 4, 5, 6 } : Array.Empty<int>();

                    int CountMatches(int[] run)
                    {
                        int count = 0;
                        for (int i = 0; i < run.Length; i++)
                        {
                            if (dice.Contains(run[i]))
                            {
                                count++;
                            }
                        }
                        return count;
                    }

                    var bestRun = runA;
                    int bestCount = CountMatches(runA);

                    int countB = CountMatches(runB);
                    if (countB > bestCount)
                    {
                        bestCount = countB;
                        bestRun = runB;
                    }

                    if (category == "Small Straight")
                    {
                        int countC = CountMatches(runC);
                        if (countC > bestCount)
                        {
                            bestRun = runC;
                        }
                    }

                    var used = new HashSet<int>();
                    for (int i = 0; i < dice.Length; i++)
                    {
                        if (bestRun.Contains(dice[i]) && !used.Contains(dice[i]))
                        {
                            hold[i] = true;
                            used.Add(dice[i]);
                        }
                    }

                    return hold;
                }

                return hold;
            }

            // Returns C(n, k) for small n — sufficient for up to 5 dice.
            static double BinomCoeff(int n, int k)
            {
                if (k < 0 || k > n) return 0d;
                if (k > n - k) k = n - k;
                double result = 1d;
                for (int i = 0; i < k; i++)
                {
                    result *= (n - i);
                    result /= (i + 1);
                }
                return result;
            }

            // Computes the exact probability of accumulating at least `threshold` dice
            // showing `targetFace` within `rolls` rolls, using a Markov chain over
            // the number of matching dice held. This avoids Monte Carlo noise for
            // upper section categories, ensuring symmetric inputs yield identical results.
            static double UpperSectionExactProbability(int[] dice, int targetFace, int rolls, int threshold)
            {
                int initial = 0;
                foreach (var d in dice)
                    if (d == targetFace) initial++;

                if (initial >= threshold) return 1d;
                if (rolls == 0) return 0d;

                // prob[c] = probability of currently holding exactly c matching dice
                var prob = new double[6];
                prob[initial] = 1d;

                for (int roll = 0; roll < rolls; roll++)
                {
                    var next = new double[6];

                    for (int c = 0; c < threshold; c++)
                    {
                        if (prob[c] == 0d) continue;

                        int free = 5 - c; // number of dice being rerolled

                        // New matches this roll follow Binomial(free, 1/6)
                        for (int j = 0; j <= free; j++)
                        {
                            int newC = Math.Min(c + j, 5);
                            double p = BinomCoeff(free, j) * Math.Pow(1.0 / 6.0, j) * Math.Pow(5.0 / 6.0, free - j);
                            next[newC] += prob[c] * p;
                        }
                    }

                    // Absorbing states (already at threshold) carry forward unchanged
                    for (int c = threshold; c <= 5; c++)
                        next[c] += prob[c];

                    prob = next;
                }

                double success = 0d;
                for (int c = threshold; c <= 5; c++)
                    success += prob[c];
                return success;
            }

            double EstimateCategoryProbability(string category)
            {
                // If no rolls are left, the result is already determined — return 1 if already complete, 0 if not.
                if (rollsRemaining == 0)
                {
                    return IsCategoryComplete(category, startingDice) ? 1d : 0d;
                }

                // If the starting dice already satisfy the category, every trial would succeed — short-circuit.
                if (IsCategoryComplete(category, startingDice))
                {
                    return 1d;
                }

                // Upper section categories have an exact closed-form solution via Markov chain.
                // Using this avoids Monte Carlo noise that would make symmetric categories
                // (e.g. Twos and Threes with no matching dice) show different probabilities.
                int upperFace = category switch
                {
                    "Aces" => 1, "Twos" => 2, "Threes" => 3,
                    "Fours" => 4, "Fives" => 5, "Sixes" => 6,
                    _ => 0
                };
                if (upperFace > 0)
                {
                    int threshold = category switch
                    {
                        "Aces" => 3, "Twos" => 3, "Threes" => 3,
                        "Fours" => 3, "Fives" => 3, "Sixes" => 3,
                        _ => 1
                    };
                    return UpperSectionExactProbability(startingDice, upperFace, rollsRemaining, threshold);
                }

                // Run a Monte Carlo simulation with 4000 trials to estimate the probability.
                const int trials = 4000;
                int successes = 0;

                for (int trial = 0; trial < trials; trial++)
                {
                    // Start each trial with a fresh copy of the current dice values.
                    var simDice = (int[])startingDice.Clone();

                    // Simulate each remaining roll, using the hold strategy to keep beneficial dice.
                    for (int roll = 0; roll < rollsRemaining; roll++)
                    {
                        // Stop rolling early if the category is already complete.
                        if (IsCategoryComplete(category, simDice))
                        {
                            break;
                        }

                        // Determine which dice to keep, then reroll the rest randomly.
                        var hold = BuildHoldMask(category, simDice);
                        for (int i = 0; i < simDice.Length; i++)
                        {
                            if (!hold[i])
                            {
                                simDice[i] = Random.Shared.Next(1, 7);
                            }
                        }
                    }

                    // After all rolls, check if the trial ended in success.
                    if (IsCategoryComplete(category, simDice))
                    {
                        successes++;
                    }
                }

                // Return the fraction of trials that succeeded as the estimated probability.
                return (double)successes / trials;
            }

            // Run the estimator for every category and store the results.
            foreach (var category in categories)
            {
                result[category] = EstimateCategoryProbability(category);
            }

            return result;
        }

        public void RollDice()
        {
            if (TurnCount >= turnsPerGame)
            {
                return;
            }

            _lastSnapshot = new GameSnapshot
            {
                RollCount = RollCount,
                TurnCount = TurnCount,
                DiceValues = GameDice.Select(d => d.Value).ToArray(),
                Holds = GameDice.Select(d => d.Hold).ToArray()
            };

            foreach (var dice in GameDice)
            {
                dice.Roll();
            }

            RollCount++;

            if (RollCount >= rollsPerTurn)
            {
                RollCount = 0;
                TurnCount++;

                foreach (var dice in GameDice)
                {
                    dice.Value = 0;
                    dice.Hold = false;
                }
            }

            if (TurnCount < turnsPerGame)
            {
                Form1.ScorecardInstance?.SetProbabilities(CalculateProbabilities(GameDice, rollsPerTurn - RollCount));
            }
        }

        public bool Undo()
        {
            if (_lastSnapshot == null)
            {
                return false;
            }

            RollCount = _lastSnapshot.RollCount;
            TurnCount = _lastSnapshot.TurnCount;

            for (int i = 0; i < GameDice.Count; i++)
            {
                GameDice[i].Value = _lastSnapshot.DiceValues[i];
                GameDice[i].Hold = _lastSnapshot.Holds[i];
            }

            _lastSnapshot = null;
            Form1.ScorecardInstance?.SetProbabilities(CalculateProbabilities(GameDice, rollsPerTurn - RollCount));
            return true;
        }
    }
}
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace YahtzeeSim
{
    public sealed class ScoreSelectionEventArgs : EventArgs
    {
        public int GameIndex { get; }
        public string Category { get; }

        public ScoreSelectionEventArgs(int gameIndex, string category)
        {
            GameIndex = gameIndex;
            Category = category;
        }
    }

    public class ScorecardControl : Control
    {
        private readonly string[] categories = new string[]
        {
        // Upper Section
        "Aces", "Twos", "Threes", "Fours", "Fives", "Sixes",
        // Lower Section
        "Three of a Kind", "Four of a Kind", "Full House",
        "Small Straight", "Large Straight", "Yahtzee", "Chance",
        "Total"
        };

        private const int GameCount = 6;
        private readonly Dictionary<string, int?>[] gameScores;
        private readonly Dictionary<string, string> probabilityDisplay;
        private int colWidth;
        private int rowHeight = 25;
        private bool scoreSelectionEnabled;

        public event EventHandler<ScoreSelectionEventArgs>? ScoreCellClicked;
        public int GameCountValue => GameCount;

        private int GetColumnWidth()
        {
            int calculated = this.Width / (GameCount + 2);
            return Math.Clamp(calculated, 105, 150);
        }

        public ScorecardControl()
        {
            this.DoubleBuffered = true;
            this.BackColor = Color.White;
            this.SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw | ControlStyles.UserPaint, true);
            this.UpdateStyles();
            this.Size = new Size(700, 400);
            this.colWidth = this.Width / (GameCount + 2);
            gameScores = new Dictionary<string, int?>[GameCount];
            probabilityDisplay = new Dictionary<string, string>();
            this.MouseClick += ScorecardControl_MouseClick;

            for (int i = 0; i < GameCount; i++)
            {
                gameScores[i] = new Dictionary<string, int?>();
                foreach (var category in categories)
                    gameScores[i][category] = null;
            }

            foreach (var category in categories)
            {
                probabilityDisplay[category] = string.Empty;
            }
        }

        public int GetGameTotal(int gameIndex)
        {
            if (gameIndex < 0 || gameIndex >= GameCount)
            {
                return 0;
            }

            return gameScores[gameIndex]["Total"] ?? 0;
        }

        public void SetScoreSelectionEnabled(bool enabled)
        {
            if (scoreSelectionEnabled == enabled)
            {
                return;
            }

            scoreSelectionEnabled = enabled;
            Invalidate();
        }

        private void ScorecardControl_MouseClick(object? sender, MouseEventArgs e)
        {
            int colWidth = GetColumnWidth();
            int col = e.X / colWidth;
            int row = (e.Y / rowHeight) - 1;

            if (col >= 2 && col < GameCount + 2 && row >= 0 && row < categories.Length)
            {
                string category = categories[row];
                if (category != "Total")
                {
                    ScoreCellClicked?.Invoke(this, new ScoreSelectionEventArgs(col - 2, category));
                }
            }
        }

        public bool HasScore(int gameIndex, string category)
        {
            if (gameIndex < 0 || gameIndex >= GameCount || !gameScores[gameIndex].ContainsKey(category))
            {
                return false;
            }

            return gameScores[gameIndex][category].HasValue;
        }

        public void SetScore(int gameIndex, string category, int score)
        {
            if (gameIndex >= 0 && gameIndex < GameCount &&
                gameScores[gameIndex].ContainsKey(category))
            {
                gameScores[gameIndex][category] = score;
                RecalculateTotal(gameIndex);
                Invalidate();
            }
        }

        public void ClearScore(int gameIndex, string category)
        {
            if (gameIndex >= 0 && gameIndex < GameCount &&
                gameScores[gameIndex].ContainsKey(category))
            {
                gameScores[gameIndex][category] = null;
                RecalculateTotal(gameIndex);
                Invalidate();
            }
        }

        private void RecalculateTotal(int gameIndex)
        {
            int total = 0;
            foreach (var category in categories)
            {
                if (category == "Total")
                {
                    continue;
                }

                total += gameScores[gameIndex][category] ?? 0;
            }

            gameScores[gameIndex]["Total"] = total;
        }

        public void SetProbabilities(Dictionary<string, double> probabilities)
        {
            if (probabilities == null)
            {
                return;
            }

            for (int i = 0; i < categories.Length; i++)
            {
                string category = categories[i];
                if (category == "Total")
                {
                    probabilityDisplay[category] = string.Empty;
                    continue;
                }

                if (probabilities.TryGetValue(category, out double value))
                {
                    value = Math.Clamp(value, 0d, 1d);
                    probabilityDisplay[category] = $"{value:P1}";
                }
                else
                {
                    probabilityDisplay[category] = string.Empty;
                }
            }

            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            Graphics g = e.Graphics;
            g.Clear(this.BackColor);
            int rowHeight = 25;
            int colWidth = GetColumnWidth();
            int y = 0;

            // Draw headers
            for (int i = 0; i <= GameCount + 1; i++)
            {
                Rectangle header = new Rectangle(i * colWidth, y, colWidth, rowHeight);
                g.FillRectangle(Brushes.LightGray, header);
                g.DrawRectangle(Pens.Black, header);
                string text = i switch
                {
                    0 => "Category",
                    1 => "Probability",
                    _ => $"Game {i - 1}"
                };
                TextRenderer.DrawText(g, text, this.Font, new Point(header.X + 5, header.Y + 5), Color.Black);
            }

            y += rowHeight;

            // Draw rows
            foreach (var category in categories)
            {
                for (int i = 0; i <= GameCount + 1; i++)
                {
                    Rectangle rect = new Rectangle(i * colWidth, y, colWidth, rowHeight);
                    Brush background = Brushes.White;
                    if (i >= 2 && category != "Total")
                    {
                        bool hasScore = gameScores[i - 2][category].HasValue;
                        if (scoreSelectionEnabled)
                        {
                            background = hasScore ? Brushes.Gainsboro : Brushes.LightYellow;
                        }
                        else if (hasScore)
                        {
                            background = Brushes.WhiteSmoke;
                        }
                    }

                    g.FillRectangle(background, rect);
                    g.DrawRectangle(Pens.Black, rect);

                    string text = i switch
                    {
                        0 => category,
                        1 => probabilityDisplay[category],
                        _ => gameScores[i - 2][category]?.ToString() ?? string.Empty
                    };
                    TextRenderer.DrawText(g, text, this.Font, new Point(rect.X + 5, rect.Y + 5), Color.Black);
                }
                y += rowHeight;
            }
        }
    }
}

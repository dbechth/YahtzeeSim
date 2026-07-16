namespace YahtzeeSim
{
    public partial class Form1 : Form
    {
        Game game;
        internal static ScorecardControl? ScorecardInstance { get; private set; }

        public Form1()
        {
            InitializeComponent();
            ScorecardInstance = Scorecard;
        }

        private void Form1_Load(object sender, EventArgs e)
        {
            game = new Game();
            UpdateDiceDisplay();
            UpdateCountLabels();
        }

        private void btnNewGame_Click(object sender, EventArgs e)
        {
            game = new Game();
            UpdateDiceDisplay();
            UpdateCountLabels();
        }

        private void btnRoll_Click(object sender, EventArgs e)
        {
            game.RollDice();
            UpdateDiceDisplay();
            UpdateCountLabels();
        }

        private void btnUndo_Click(object sender, EventArgs e)
        {
            if (game.Undo())
            {
                UpdateDiceDisplay();
                UpdateCountLabels();
            }
        }

        private void UpdateDiceDisplay()
        {
            diceControl1.DiceValue = game.GameDice.Single(x => x.ID == 1).Value;
            diceControl2.DiceValue = game.GameDice.Single(x => x.ID == 2).Value;
            diceControl3.DiceValue = game.GameDice.Single(x => x.ID == 3).Value;
            diceControl4.DiceValue = game.GameDice.Single(x => x.ID == 4).Value;
            diceControl5.DiceValue = game.GameDice.Single(x => x.ID == 5).Value;
        }

        private void UpdateCountLabels()
        {
            label1.Text = $"Turn Count: {game.TurnCount}";
            label2.Text = $"Roll Count: {game.RollCount}";
        }

        private void button1_Click(object sender, EventArgs e)
        {

        }
    }
}

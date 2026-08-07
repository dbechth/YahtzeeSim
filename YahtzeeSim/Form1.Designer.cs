namespace YahtzeeSim
{
    partial class Form1
    {
        /// <summary>
        ///  Required designer variable.
        /// </summary>
        private System.ComponentModel.IContainer components = null;

        /// <summary>
        ///  Clean up any resources being used.
        /// </summary>
        /// <param name="disposing">true if managed resources should be disposed; otherwise, false.</param>
        protected override void Dispose(bool disposing)
        {
            if (disposing && (components != null))
            {
                components.Dispose();
            }
            base.Dispose(disposing);
        }

        #region Windows Form Designer generated code

        /// <summary>
        ///  Required method for Designer support - do not modify
        ///  the contents of this method with the code editor.
        /// </summary>
        private void InitializeComponent()
        {
            mainLayout = new TableLayoutPanel();
            topPanel = new Panel();
            statusPanel = new FlowLayoutPanel();
            btnRoll = new Button();
            btnUndo = new Button();
            btnNewGame = new Button();
            label1 = new Label();
            label2 = new Label();
            dicePanel = new FlowLayoutPanel();
            diceControl1 = new DiceControl();
            diceControl2 = new DiceControl();
            diceControl3 = new DiceControl();
            diceControl4 = new DiceControl();
            diceControl5 = new DiceControl();
            actionPanel = new FlowLayoutPanel();
            Scorecard = new ScorecardControl();
            mainLayout.SuspendLayout();
            topPanel.SuspendLayout();
            statusPanel.SuspendLayout();
            dicePanel.SuspendLayout();
            actionPanel.SuspendLayout();
            SuspendLayout();
            // 
            // mainLayout
            // 
            mainLayout.ColumnCount = 1;
            mainLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            mainLayout.Controls.Add(topPanel, 0, 0);
            mainLayout.Controls.Add(Scorecard, 0, 1);
            mainLayout.Dock = DockStyle.Fill;
            mainLayout.Location = new Point(0, 0);
            mainLayout.Name = "mainLayout";
            mainLayout.RowCount = 2;
            mainLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 280F));
            mainLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            mainLayout.Size = new Size(980, 641);
            mainLayout.TabIndex = 0;
            // 
            // topPanel
            // 
            topPanel.Controls.Add(actionPanel);
            topPanel.Controls.Add(dicePanel);
            topPanel.Controls.Add(statusPanel);
            topPanel.Dock = DockStyle.Fill;
            topPanel.Location = new Point(3, 3);
            topPanel.Name = "topPanel";
            topPanel.Padding = new Padding(12);
            topPanel.Size = new Size(974, 274);
            topPanel.TabIndex = 0;
            // 
            // statusPanel
            // 
            statusPanel.AutoSize = true;
            statusPanel.Controls.Add(label1);
            statusPanel.Controls.Add(label2);
            statusPanel.Location = new Point(12, 12);
            statusPanel.Name = "statusPanel";
            statusPanel.Size = new Size(390, 32);
            statusPanel.TabIndex = 0;
            // 
            // btnRoll
            // 
            btnRoll.BackColor = Color.FromArgb(46, 174, 222);
            btnRoll.Cursor = Cursors.Hand;
            btnRoll.FlatStyle = FlatStyle.Flat;
            btnRoll.Font = new Font("Segoe UI Semibold", 10F);
            btnRoll.ForeColor = Color.White;
            btnRoll.Location = new Point(3, 53);
            btnRoll.Name = "btnRoll";
            btnRoll.Size = new Size(170, 44);
            btnRoll.TabIndex = 2;
            btnRoll.Text = "Roll";
            btnRoll.UseVisualStyleBackColor = false;
            btnRoll.Click += btnRoll_Click;
            // 
            // btnUndo
            // 
            btnUndo.BackColor = Color.FromArgb(221, 127, 21);
            btnUndo.Cursor = Cursors.Hand;
            btnUndo.FlatStyle = FlatStyle.Flat;
            btnUndo.Font = new Font("Segoe UI Semibold", 10F);
            btnUndo.ForeColor = Color.White;
            btnUndo.Location = new Point(3, 103);
            btnUndo.Name = "btnUndo";
            btnUndo.Size = new Size(170, 44);
            btnUndo.TabIndex = 3;
            btnUndo.Text = "Undo";
            btnUndo.UseVisualStyleBackColor = false;
            btnUndo.Click += btnUndo_Click;
            // 
            // btnNewGame
            // 
            btnNewGame.BackColor = Color.FromArgb(86, 100, 115);
            btnNewGame.Cursor = Cursors.Hand;
            btnNewGame.FlatStyle = FlatStyle.Flat;
            btnNewGame.Font = new Font("Segoe UI Semibold", 10F);
            btnNewGame.ForeColor = Color.White;
            btnNewGame.Location = new Point(3, 3);
            btnNewGame.Name = "btnNewGame";
            btnNewGame.Size = new Size(170, 44);
            btnNewGame.TabIndex = 3;
            btnNewGame.Text = "New Game";
            btnNewGame.UseVisualStyleBackColor = false;
            btnNewGame.Click += btnNewGame_Click;
            // 
            // label1
            // 
            label1.AutoSize = false;
            label1.BackColor = Color.FromArgb(232, 237, 245);
            label1.BorderStyle = BorderStyle.FixedSingle;
            label1.Font = new Font("Segoe UI Semibold", 10F);
            label1.Location = new Point(3, 3);
            label1.Margin = new Padding(3);
            label1.Name = "label1";
            label1.Size = new Size(190, 26);
            label1.TabIndex = 4;
            label1.Text = "Turn Count";
            label1.TextAlign = ContentAlignment.MiddleLeft;
            // 
            // label2
            // 
            label2.AutoSize = false;
            label2.BackColor = Color.FromArgb(232, 237, 245);
            label2.BorderStyle = BorderStyle.FixedSingle;
            label2.Font = new Font("Segoe UI Semibold", 10F);
            label2.Location = new Point(199, 3);
            label2.Margin = new Padding(3);
            label2.Name = "label2";
            label2.Size = new Size(190, 26);
            label2.TabIndex = 5;
            label2.Text = "Roll Count";
            label2.TextAlign = ContentAlignment.MiddleLeft;
            // 
            // dicePanel
            // 
            dicePanel.AutoSize = false;
            dicePanel.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            dicePanel.Controls.Add(diceControl1);
            dicePanel.Controls.Add(diceControl2);
            dicePanel.Controls.Add(diceControl3);
            dicePanel.Controls.Add(diceControl4);
            dicePanel.Controls.Add(diceControl5);
            dicePanel.Location = new Point(12, 56);
            dicePanel.Name = "dicePanel";
            dicePanel.Size = new Size(540, 106);
            dicePanel.TabIndex = 1;
            dicePanel.WrapContents = false;
            dicePanel.Padding = new Padding(0, 3, 0, 3);
            // 
            // diceControl1
            // 
            diceControl1.DiceValue = 1;
            diceControl1.Location = new Point(3, 3);
            diceControl1.Margin = new Padding(3, 0, 3, 0);
            diceControl1.Name = "diceControl1";
            diceControl1.Size = new Size(100, 100);
            diceControl1.TabIndex = 6;
            diceControl1.TabStop = false;
            // 
            // diceControl2
            // 
            diceControl2.DiceValue = 1;
            diceControl2.Location = new Point(109, 3);
            diceControl2.Margin = new Padding(3, 0, 3, 0);
            diceControl2.Name = "diceControl2";
            diceControl2.Size = new Size(100, 100);
            diceControl2.TabIndex = 7;
            diceControl2.TabStop = false;
            // 
            // diceControl3
            // 
            diceControl3.DiceValue = 1;
            diceControl3.Location = new Point(215, 3);
            diceControl3.Margin = new Padding(3, 0, 3, 0);
            diceControl3.Name = "diceControl3";
            diceControl3.Size = new Size(100, 100);
            diceControl3.TabIndex = 8;
            diceControl3.TabStop = false;
            // 
            // diceControl4
            // 
            diceControl4.DiceValue = 1;
            diceControl4.Location = new Point(321, 3);
            diceControl4.Margin = new Padding(3, 0, 3, 0);
            diceControl4.Name = "diceControl4";
            diceControl4.Size = new Size(100, 100);
            diceControl4.TabIndex = 9;
            diceControl4.TabStop = false;
            // 
            // diceControl5
            // 
            diceControl5.DiceValue = 1;
            diceControl5.Location = new Point(427, 3);
            diceControl5.Margin = new Padding(3, 0, 3, 0);
            diceControl5.Name = "diceControl5";
            diceControl5.Size = new Size(100, 100);
            diceControl5.TabIndex = 10;
            diceControl5.TabStop = false;
            // 
            // actionPanel
            // 
            actionPanel.Controls.Add(btnNewGame);
            actionPanel.Controls.Add(btnRoll);
            actionPanel.Controls.Add(btnUndo);
            actionPanel.Dock = DockStyle.Right;
            actionPanel.FlowDirection = FlowDirection.TopDown;
            actionPanel.Location = new Point(786, 12);
            actionPanel.Name = "actionPanel";
            actionPanel.Size = new Size(176, 150);
            actionPanel.TabIndex = 2;
            actionPanel.WrapContents = false;
            // 
            // Scorecard
            // 
            Scorecard.Dock = DockStyle.Fill;
            Scorecard.Location = new Point(3, 283);
            Scorecard.Margin = new Padding(3, 0, 3, 3);
            Scorecard.Name = "Scorecard";
            Scorecard.Size = new Size(974, 355);
            Scorecard.TabIndex = 11;
            Scorecard.Text = "Scorecard";
            Scorecard.Click += button1_Click;
            // 
            // Form1
            // 
            AutoScaleDimensions = new SizeF(7F, 15F);
            AutoScaleMode = AutoScaleMode.Font;
            BackColor = Color.FromArgb(245, 247, 250);
            ClientSize = new Size(980, 641);
            Controls.Add(mainLayout);
            MinimumSize = new Size(996, 680);
            Name = "Form1";
            StartPosition = FormStartPosition.CenterScreen;
            Text = "YahtzeeSim";
            Load += Form1_Load;
            mainLayout.ResumeLayout(false);
            topPanel.ResumeLayout(false);
            topPanel.PerformLayout();
            statusPanel.ResumeLayout(false);
            dicePanel.ResumeLayout(false);
            actionPanel.ResumeLayout(false);
            ResumeLayout(false);
        }

        #endregion

        private TableLayoutPanel mainLayout;
        private Panel topPanel;
        private FlowLayoutPanel statusPanel;
        private FlowLayoutPanel dicePanel;
        private FlowLayoutPanel actionPanel;
        private Button btnRoll;
        private Button btnUndo;
        private Button btnNewGame;
        private Label label1;
        private Label label2;
        private DiceControl diceControl1;
        private DiceControl diceControl2;
        private DiceControl diceControl3;
        private DiceControl diceControl4;
        private DiceControl diceControl5;
        private ScorecardControl Scorecard;
    }
}

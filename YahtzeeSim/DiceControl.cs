using System;
using System.Drawing;
using System.Windows.Forms;
using YahtzeeSim;

namespace YahtzeeSim
{
    public class DiceControl : Control
    {
        private int diceValue = 0;
        private Dice? associatedDice = null;

        public int DiceValue
        {
            get => diceValue;
            set
            {
                if (value >= 0 && value <= 6)
                {
                    diceValue = value;
                    Invalidate(); // Redraw the control
                }
            }
        }

        internal Dice? AssociatedDice
        {
            get => associatedDice;
            set
            {
                associatedDice = value;
            }
        }

        public DiceControl()
        {
            this.DoubleBuffered = true;
            this.Size = new Size(100, 100);
            this.Click += DiceControl_Click;
        }

        private void DiceControl_Click(object? sender, EventArgs e)
        {
            if (associatedDice != null && diceValue > 0)
            {
                associatedDice.Hold = !associatedDice.Hold;
                Invalidate();
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            Graphics g = e.Graphics;

            // Draw background dice face
            g.FillRectangle(Brushes.White, this.ClientRectangle);
            
            // Draw hold indicator border if held
            if (associatedDice?.Hold == true)
            {
                using (Pen holdPen = new Pen(Color.Red, 4))
                {
                    g.DrawRectangle(holdPen, 2, 2, this.Width - 5, this.Height - 5);
                }
            }
            else
            {
                g.DrawRectangle(Pens.Black, 0, 0, this.Width - 1, this.Height - 1);
            }

            // Draw dice dots
            DrawDots(g);

            // Draw numerical value
            //using (Font font = new Font("Arial", 12, FontStyle.Bold))
            //{
            //    TextRenderer.DrawText(g, $"Value: {diceValue}", font, new Point(5, this.Height - ), Color.Black);
            //}
        }

        private void DrawDots(Graphics g)
        {
            int size = 12;
            int offset = 20;
            int centerX = this.Width / 2;
            int centerY = this.Height / 2;

            void Dot(int x, int y) => g.FillEllipse(Brushes.Black, x - size / 2, y - size / 2, size, size);

            switch (diceValue)
            {
                case 1:
                    Dot(centerX, centerY);
                    break;
                case 2:
                    Dot(offset, offset);
                    Dot(this.Width - offset, this.Height - offset);
                    break;
                case 3:
                    Dot(offset, offset);
                    Dot(centerX, centerY);
                    Dot(this.Width - offset, this.Height - offset);
                    break;
                case 4:
                    Dot(offset, offset);
                    Dot(offset, this.Height - offset);
                    Dot(this.Width - offset, offset);
                    Dot(this.Width - offset, this.Height - offset);
                    break;
                case 5:
                    Dot(offset, offset);
                    Dot(offset, this.Height - offset);
                    Dot(this.Width - offset, offset);
                    Dot(this.Width - offset, this.Height - offset);
                    Dot(centerX, centerY);
                    break;
                case 6:
                    Dot(offset, offset);
                    Dot(offset, centerY);
                    Dot(offset, this.Height - offset);
                    Dot(this.Width - offset, offset);
                    Dot(this.Width - offset, centerY);
                    Dot(this.Width - offset, this.Height - offset);
                    break;
            }
        }
    }
}

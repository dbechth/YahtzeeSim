using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace YahtzeeSim
{
    internal class Dice
    {
        public int ID { get; set; }
        public int Value { get; set; }
        public bool Hold { get; set; }
        public Random random { get; set; }

        public Dice()
        {
            this.ID = -1;
            this.Value= 0;
            this.Hold = false;
            random = new Random();
        }

        public void Roll()
        {
            if (!Hold)
            {
                this.Value = random.Next(1, 7);
            }
        }
    }
}
using System;
using System.Collections.Generic;
using System.Linq;
using System.Web;

namespace celtraJackpotPlayer.Models
{
    public class Game
    {
        public int GameID { get; set; }
        public string GameLocation { get; set; }
        public int Machines { get; set; }
        public int Pulls { get; set; }
        public int NumOfPlays { get; set; }

        public int NumOfSections { get; set; }
        public int[] Sections { get; set; }
        public float[,] Probabilities { get; set; }
        public int[,] SectionsScore { get; set; }
        public int[,] SectionsCount { get; set; }
        public bool isConstant { get; set; }

        public int[] Score { get; set; }
    }
}
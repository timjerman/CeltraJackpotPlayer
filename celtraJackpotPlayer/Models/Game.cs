using System;
using System.Collections.Generic;
using System.Linq;
using System.Web;

namespace celtraJackpotPlayer.Models
{
    public enum isProbabilityConstant
    {
        NotConstant,
        LowCertainty,
        HighCertainty
    };

    public class Game
    {
        public int GameID { get; set; }
        public DateTime LastGameTime { get; set; }
        public string GameLocation { get; set; }
        public int Machines { get; set; }
        public int Pulls { get; set; }
        public int NumOfPlays { get; set; }

        public int NumOfSections { get; set; }
        public isProbabilityConstant isConstant { get; set; }

        public int[] Sections { get; set; }
        public string SectionsStr { get; set; } // database entry
        public int[,] Probabilities { get; set; }
        public string ProbabilitiesStr { get; set; } // database entry
        public int[,] SectionsScore { get; set; }
        public string SectionsScoreStr { get; set; } // database entry
        public int[,] SectionsCount { get; set; }
        public string SectionsCountStr { get; set; } // database entry

        public int[] Score { get; set; }
        public string ScoreStr { get; set; } // database entry
    }
}
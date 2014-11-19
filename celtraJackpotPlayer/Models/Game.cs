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

    public enum sectionsInUse
    {
        Custom,
        Detailed,
        VeryDetailed
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

        // more detailed sections for long term selection improvement
        public int[,] SectionsScore50 { get; set; }
        public string SectionsScore50Str { get; set; } // database entry
        public int[,] SectionsCount50 { get; set; }
        public string SectionsCount50Str { get; set; } // database entry
        public int[,] SectionsScore100 { get; set; }
        public string SectionsScore100Str { get; set; } // database entry
        public int[,] SectionsCount100 { get; set; }
        public string SectionsCount100Str { get; set; } // database entry
        public sectionsInUse SectionStatus { get; set; }


        public int[] Score { get; set; }
        public string ScoreStr { get; set; } // database entry
    }
}
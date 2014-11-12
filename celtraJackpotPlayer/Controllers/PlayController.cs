using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Net;
using System.Threading;
using System.Web;
using System.Web.Mvc;
using celtraJackpotPlayer.Models;

namespace celtraJackpotPlayer.Controllers
{
    public class PlayController : Controller
    {
        //private static readonly ThreadLocal<Random> rndGen = new ThreadLocal<Random>(() => new Random());
        private static Random rndGen = new Random();

        //
        // GET: /Play/Start

        public object Start()//(string address)
        {
            _SetPlayerPlayState(true);
            _SetPlayerPlayProgress(0);

            string address = "http://celtra-jackpot.com/1";

            // load game from db if exists else initialiaze a new object
            Game gameData = _InitializeGameData(address);
            if (gameData == null)
            {
                _SetPlayerPlayState(false);
                _AddLogToDb("An error occurred during the computation!", false);
                return Content("ERR", "text/plain");
            }

            // main call
            gameData = _Play(gameData);
            //gameData = _TestPlay(gameData);

            // check for success
            if (gameData == null)
            {
                _SetPlayerPlayState(false);
                _AddLogToDb("An error occurred during the computation!", false);
                return Content("ERR", "text/plain");
            }

            // store data and return last score
            gameData = _UpdateGameObjectForDbSave(gameData);
            if (gameData.NumOfPlays > 1)
                _SaveGameToDb(gameData);
            else
                _AddGameToDb(gameData);

            int resultingScore = gameData.Score[gameData.NumOfPlays - 1];
            _AddLogToDb("score: " + resultingScore.ToString() + " (sum: " + _SumOfScores(gameData) + "), game: " + address, true);
            _SetPlayerPlayState(false);

            return Content(resultingScore.ToString(), "text/plain");
        }


        // ---------------------------------------------------------------------------------
        // ---------------------------- MAIN CALCULATION - BEGIN ---------------------------
        // ----------------------------------------------------------------------------------

        // main method which tries to selects the machines so that the score is maximized
        private Game _Play(Game gameData)
        {
            // parameters
            byte sectionStopCriteria = 5; // when a machine gathers this score create a new section
            byte maxSectionFraction = 10; // what is the maximum possible section -> all pulls/this

            int score = 0;
            int progress = 0;

            gameData.NumOfPlays++;

            // --------------------------  FIRST PLAY OF A PARTICULAR GAME


            // if this is the first run of the game calculate the segments
            // the selection probability for the first run is the same for every machine
            if (gameData.NumOfPlays == 1)
            {
                // --------------------------  INITIAL MACHINE WEIGHTS

                // initially all machines are pulled with the same probability
                for (int machine = 0; machine < gameData.Machines; machine++)
                {
                    gameData.Probabilities[0, machine] = 100 / gameData.Machines;   // probabilities are converted to cummulative sum
                    if (machine > 0)
                        gameData.Probabilities[0, machine] += gameData.Probabilities[0, machine - 1];
                }

                // --------------------------   SECTION CREATION

                bool searhForNewSection = true;
                bool forceNewSection = false;
                int sectionIndex = 0;
                int expextedSectionIndex = 0;
                int[] scorePerSection = new int[gameData.Machines];
                for (int i = 0; i < scorePerSection.Length; i++)
                    scorePerSection[i] = 0;

                // go through all availible pulls and set the section
                for (int pull = 1; pull <= gameData.Pulls; pull++)
                {

                    byte selectedMachine = _selectMachine(gameData.Probabilities, 0);

                    int machineScore = _GetMachinePullScore(gameData.GameLocation, selectedMachine, pull);
                    if (machineScore < 0)
                        return null;

                    score += machineScore;
                    scorePerSection[selectedMachine - 1] += machineScore;

                    // accumulate section probabilities
                    gameData.SectionsScore[sectionIndex, selectedMachine - 1] += machineScore;
                    gameData.SectionsCount[sectionIndex, selectedMachine - 1]++;

                    // check if a new section is found
                    if (((scorePerSection.Max() >= sectionStopCriteria) || forceNewSection) && searhForNewSection)
                    {
                        gameData.Sections[sectionIndex] = ((int)Math.Ceiling(pull / 10.0)) * 10; // round the new section
                        searhForNewSection = false;
                        forceNewSection = false;
                    }

                    // when the section border is hit progress to the next one
                    if (pull == gameData.Sections[sectionIndex])
                    {
                        sectionIndex++;
                        for (int i = 0; i < scorePerSection.Length; i++) //reset
                            scorePerSection[i] = 0;
                        searhForNewSection = true;
                        if (sectionIndex > (gameData.Sections.Length - 1)) //resize all arays if they are too small
                            gameData = _ResizeGameDataArrays(gameData);
                    }

                    // lets make the sections at least the size of all pulls / maxSectionFraction
                    if ((pull % (gameData.Pulls / maxSectionFraction - 1)) == 0 && scorePerSection.Max() > 0)
                    {
                        if (sectionIndex < expextedSectionIndex)
                        {
                            forceNewSection = true;
                            searhForNewSection = true;
                        }
                        expextedSectionIndex++;
                    }

                    // save progress for web display
                    if (pull % (gameData.Pulls / 100) == 0)
                        _SetPlayerPlayProgress(++progress);
                }

                if (gameData.Sections[sectionIndex - 1] != gameData.Pulls)
                    gameData.Sections[sectionIndex - 1] = gameData.Pulls;
                else
                    sectionIndex--;


                // if the last section has too few scores -> merge last two sections
                int maxVal = 0;
                for (int i = 0; i < gameData.Machines; i++)
                {
                    if (gameData.SectionsScore[sectionIndex, i] > maxVal)
                        maxVal = gameData.SectionsScore[sectionIndex, i];
                }

                if (maxVal < sectionStopCriteria / 2 + 1)
                {
                    for (int i = 0; i < gameData.Machines; i++)
                    {
                        gameData.SectionsScore[sectionIndex - 1, i] += gameData.SectionsScore[sectionIndex, i];
                        gameData.SectionsScore[sectionIndex, i] = 0;
                        gameData.SectionsCount[sectionIndex - 1, i] += gameData.SectionsCount[sectionIndex, i];
                        gameData.SectionsCount[sectionIndex, i] = 0;
                    }
                    gameData.Sections[sectionIndex - 1] += gameData.Sections[sectionIndex];
                    gameData.Sections[sectionIndex] = 0;
                    sectionIndex--;
                }

                gameData.NumOfSections = sectionIndex;
            }

            gameData.Score[gameData.NumOfPlays - 1] = score;

            return gameData;
        }


        // ----------------------------------------------------------------------------------
        // ---------------------------- MAIN CALCULATION - END -----------------------------
        // ---------------------------------------------------------------------------------


        // randomly select the machine based on its probability
        private byte _selectMachine(int[,] probabilities, int section)
        {
            byte selectedMachine = 1;
            int rndNumber = rndGen.Next(1, 101); // numbers 1-100

            // randomly select machine based on the weights
            for (int i = 0; i < probabilities.GetLength(1) - 1; i++)
            {
                if (rndNumber > probabilities[section, i])
                    selectedMachine++;
                else break;
            }

            return selectedMachine;  // starts from 1
        }


        private Game _ResizeGameDataArrays(Game gameData)
        {
            int[] tmpArray = gameData.Sections;
            Array.Resize<int>(ref tmpArray, gameData.Sections.Length + 50);
            gameData.Sections = tmpArray;

            int[,] tmpArray2D = gameData.SectionsCount;
            _Resize2DArray(ref tmpArray2D, gameData.SectionsCount.GetLength(1) + 50);
            gameData.SectionsCount = tmpArray2D;

            tmpArray2D = gameData.SectionsScore;
            _Resize2DArray(ref tmpArray2D, gameData.SectionsScore.GetLength(1) + 50);
            gameData.SectionsScore = tmpArray2D;

            tmpArray2D = gameData.Probabilities;
            _Resize2DArray(ref tmpArray2D, gameData.Probabilities.GetLength(1) + 50);
            gameData.Probabilities = tmpArray2D;

            return gameData;
        }

        // resizing of a 2D array
        private void _Resize2DArray(ref int[,] array, int newSize)
        {
            int[,] tmpArray = new int[newSize, array.GetLength(1)];
            for (int i = 0; i < newSize; i++)
                for (int j = 0; j < array.GetLength(1); j++)
                    tmpArray[i, j] = array[i, j];
            array = tmpArray;
        }

        // a simple test of calling the game server and summing through the first machine
        private Game _TestPlay(Game gameData)
        {
            gameData.NumOfPlays++;

            int score = 0;
            int progress = 0;

            for (int pullNumber = 1; pullNumber <= gameData.Pulls; pullNumber++)
            {
                score += _GetMachinePullScore(gameData.GameLocation, 1, pullNumber);

                if (pullNumber % (gameData.Pulls / 100) == 0)
                    _SetPlayerPlayProgress(++progress);
            }

            gameData.Score[gameData.NumOfPlays - 1] = score;

            return gameData;
        }


        // convert all arrays in the game objects to strings so that they can be stored in the database
        private Game _UpdateGameObjectForDbSave(Game gameData)
        {
            gameData.ScoreStr = _IntArrayToString(gameData.Score);
            gameData.SectionsStr = _IntArrayToString(gameData.Sections);
            gameData.ProbabilitiesStr = _IntMatrixToString(gameData.Probabilities);
            gameData.SectionsScoreStr = _IntMatrixToString(gameData.SectionsScore);
            gameData.SectionsCountStr = _IntMatrixToString(gameData.SectionsCount);

            return gameData;
        }

        // convert all strings (needed for storing in the db) in the game object to arrays 
        private Game _PrepareGameObjectForComputation(Game gameData)
        {
            gameData.Score = _StringToIntArray(gameData.ScoreStr);
            gameData.Sections = _StringToIntArray(gameData.SectionsStr);
            gameData.Probabilities = _StringToIntMatrix(gameData.ProbabilitiesStr);
            gameData.SectionsScore = _StringToIntMatrix(gameData.SectionsScoreStr);
            gameData.SectionsCount = _StringToIntMatrix(gameData.SectionsCountStr);

            return gameData;
        }

        // load data from database or create a new object if the entry does not exist
        private Game _InitializeGameData(string address)
        {
            Game gameData = _GetGameFromDb(address);

            if (gameData == null)
            {
                gameData = new Game();
                gameData.GameLocation = address;
                gameData.Pulls = _GetPullsNumber(address);
                if (gameData.Pulls < 0)
                    return null;
                gameData.Machines = _GetMachinesNumber(address);
                if (gameData.Machines < 0)
                    return null;
                gameData.NumOfPlays = 0;
                gameData.NumOfSections = 0;
                gameData.isConstant = false;
                gameData.Score = new int[100];
                gameData.Sections = new int[100];
                gameData.Probabilities = new int[100, gameData.Machines];
                gameData.SectionsScore = new int[100, gameData.Machines];
                gameData.SectionsCount = new int[100, gameData.Machines];
            }
            else gameData = _PrepareGameObjectForComputation(gameData);

            return gameData;
        }

        // sum all scores from the game with the same address
        private int _SumOfScores(Game gameData)
        {
            int sum = 0;
            for (int i = 0; i < gameData.NumOfPlays; i++)
                sum += gameData.Score[i];

            return sum;
        }

        // convert int array to string -> for storing in the database
        private string _IntArrayToString(int[] array)
        {
            return String.Join(";", new List<int>(array).ConvertAll(i => i.ToString()).ToArray());
        }

        // extract int array from string -> for storing in the database
        private int[] _StringToIntArray(string str)
        {
            return str.Split(';').Select(n => Convert.ToInt32(n)).ToArray();
        }

        // convert int matrix to string -> for storing in the database
        private string _IntMatrixToString(int[,] matrix)
        {
            string returnStr = "";

            for (int i = 0; i < matrix.GetLength(0); i++)
            {
                int[] array = new int[matrix.GetLength(1)];
                System.Buffer.BlockCopy(matrix, 4 * i * matrix.GetLength(1), array, 0, 4 * matrix.GetLength(1));

                if (i > 0)
                    returnStr = returnStr + ":" + _IntArrayToString(array);
                else
                    returnStr = _IntArrayToString(array);
            }

            return returnStr;
        }

        // extract matrix from string -> for storing in the database
        private int[,] _StringToIntMatrix(string str)
        {
            string[] rowStr;
            rowStr = str.Split(':');

            int columns = _StringToIntArray(rowStr[0]).Length;
            int rows = rowStr.Length;
            int[,] matrix = new int[rows, columns];

            for (int i = 0; i < rows; i++)
            {
                int[] array = _StringToIntArray(rowStr[i]);

                for (int j = 0; j < columns; j++)
                    matrix[i, j] = array[j];
            }

            return matrix;
        }


        // retrieve all logs from the database
        private List<Log> _GetLogsFromDb()
        {
            var db = new GameContext();
            List<Log> returnList = db.Logs.OrderByDescending(x => x.LogID).Take(40).ToList();

            return returnList;
        }

        // add a new log entry to the database
        private void _AddLogToDb(string message, bool isSuccess)
        {
            Log log = new Log();
            log.Message = message;
            log.LogTime = System.DateTime.Now;
            log.IsSuccess = isSuccess;

            var db = new GameContext();
            db.Logs.Add(log);
            db.SaveChanges();

            return;
        }


        // retrieve model from database by address
        private Game _GetGameFromDb(string address)
        {
            var db = new GameContext();
            return db.Games.SingleOrDefault(game => game.GameLocation == address);
        }

        // add a new game to the database
        private void _AddGameToDb(Game gameData)
        {
            var db = new GameContext();
            db.Games.Add(gameData);
            db.SaveChanges();

            return;
        }

        // update game in the database
        private void _SaveGameToDb(Game gameData)
        {
            var db = new GameContext();
            db.Entry(gameData).State = EntityState.Modified;
            db.SaveChanges();

            return;
        }

        // call the url and convert the response to a number
        private int _GetValueFromUrl(string address)
        {
            string response = "";

            try
            {
                using (WebClient client = new WebClient())
                    response = client.DownloadString(address);
            }
            catch (WebException ex)
            {
                _AddLogToDb("Error: " + ex.ToString(), false);
            }

            if (response.Equals("ERR") || response.Equals(""))
            {
                _AddLogToDb("Error: ERR string returned from address!", false);
                return -1;
            }

            return int.Parse(response);
        }

        private int _GetPullsNumber(string address)
        {
            return _GetValueFromUrl(address + "/pulls");
        }

        private int _GetMachinesNumber(string address)
        {
            return _GetValueFromUrl(address + "/machines");
        }

        private int _GetMachinePullScore(string address, int machine, int pull)
        {
            return _GetValueFromUrl(address + "/" + machine.ToString() + "/" + pull.ToString());
        }

        private void _SetPlayerPlayState(bool state)
        {
            HttpRuntime.Cache.Insert("PlayerPlaying", state);
        }

        private void _SetPlayerPlayProgress(int progess)
        {

            HttpRuntime.Cache.Insert("PlayerProgress", progess);
        }

        private bool _GetPlayerPlayState()
        {
            bool? retVal = HttpRuntime.Cache.Get("PlayerPlaying") as bool?;

            if (retVal == null) return false;

            return (bool)retVal;
        }

        // GET: /Play/PlayerPlaying
        public bool PlayerPlaying()
        {
            return _GetPlayerPlayState();
        }

        // GET: /Play/PlayerProgress
        public int PlayerProgress()
        {
            int? retVal = HttpRuntime.Cache.Get("PlayerProgress") as int?;

            if (retVal == null) return 0;

            return (int)retVal;
        }

        // GET: /Play/Log

        public ActionResult Log()
        {
            List<Log> logs = _GetLogsFromDb();

            return PartialView("_LogPartial", logs);
        }

        protected override void OnException(ExceptionContext filterContext)
        {
            _SetPlayerPlayState(false);

            //If the exeption is already handled we do nothing
            if (filterContext.ExceptionHandled)
                return;
            else
                _AddLogToDb("Error: " + filterContext.Exception.ToString(), false);

            filterContext.ExceptionHandled = true;
        }

    }
}

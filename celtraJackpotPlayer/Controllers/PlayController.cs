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
        //
        // GET: /Play/Start

        public object Start()//(string address)
        {
            _SetPlayerPlayState(true);
            _SetPlayerPlayProgress(0);

            string address = "http://celtra-jackpot.com/5";

            // load game from db if exists else initialiaze a new object
            Game gameData = _InitializeGameData(address);
            if (gameData == null)
            {
                _SetPlayerPlayState(false);
                return Content("ERR", "text/plain");
            }

            // main call
            gameData = _TestPlay(gameData);

            // check for success
            if (gameData == null)
            {
                _SetPlayerPlayState(false);
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
        public Game _UpdateGameObjectForDbSave(Game gameData)
        {
            gameData.ScoreStr = _IntArrayToString(gameData.Score);
            gameData.SectionsStr = _IntArrayToString(gameData.Sections);
            gameData.ProbabilitiesStr = _IntMatrixToString(gameData.Probabilities);
            gameData.SectionsScoreStr = _IntMatrixToString(gameData.SectionsScore);
            gameData.SectionsCountStr = _IntMatrixToString(gameData.SectionsCount);

            return gameData;
        }

        // convert all strings (needed for storing in the db) in the game object to arrays 
        public Game _PrepareGameObjectForComputation(Game gameData)
        {
            gameData.Score = _StringToIntArray(gameData.ScoreStr);
            gameData.Sections = _StringToIntArray(gameData.SectionsStr);
            gameData.Probabilities = _StringToIntMatrix(gameData.ProbabilitiesStr);
            gameData.SectionsScore = _StringToIntMatrix(gameData.SectionsScoreStr);
            gameData.SectionsCount = _StringToIntMatrix(gameData.SectionsCountStr);

            return gameData;
        }

        // load data from database or create a new object if the entry does not exist
        public Game _InitializeGameData(string address)
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
                gameData.Probabilities = new int[gameData.Machines, 100];
                gameData.SectionsScore = new int[gameData.Machines, 100];
                gameData.SectionsCount = new int[gameData.Machines, 100];
            }
            else gameData = _PrepareGameObjectForComputation(gameData);

            return gameData;
        }

        // sum all scores from the game with the same address
        public int _SumOfScores(Game gameData)
        {
            int sum = 0;
            for (int i = 0; i < gameData.NumOfPlays; i++)
                sum += gameData.Score[i];

            return sum;
        }

        // convert int array to string -> for storing in the database
        public string _IntArrayToString(int[] array)
        {
            return String.Join(";", new List<int>(array).ConvertAll(i => i.ToString()).ToArray());
        }

        // extract int array from string -> for storing in the database
        public int[] _StringToIntArray(string str)
        {
            return str.Split(';').Select(n => Convert.ToInt32(n)).ToArray();
        }

        // convert int matrix to string -> for storing in the database
        public string _IntMatrixToString(int[,] matrix)
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
        public int[,] _StringToIntMatrix(string str)
        {
            string[] rowStr;
            rowStr = str.Split(':');         

            int columns = _StringToIntArray(rowStr[0]).Length;
            int rows = rowStr.Length;
            int[,] matrix = new int[rows,columns];

            for (int i = 0; i < rows; i++)
            {
                int[] array = _StringToIntArray(rowStr[i]);

                for (int j = 0; j < columns; j++)
                    matrix[i,j] = array[j];
            }

            return matrix;
        }


        // retrieve all logs from the database
        public List<Log> _GetLogsFromDb()
        {
            var db = new GameContext();
            List<Log> returnList = db.Logs.OrderByDescending(x => x.LogID).Take(40).ToList();

            return returnList;
        }

        // add a new log entry to the database
        public void _AddLogToDb(string message, bool isSuccess)
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
        public Game _GetGameFromDb(string address)
        {
            var db = new GameContext();
            return db.Games.SingleOrDefault(game => game.GameLocation == address);
        }

        // add a new game to the database
        public void _AddGameToDb(Game gameData)
        {
            var db = new GameContext();
            db.Games.Add(gameData);
            db.SaveChanges();

            return;
        }

        // update game in the database
        public void _SaveGameToDb(Game gameData)
        {
            var db = new GameContext();
            db.Entry(gameData).State = EntityState.Modified;
            db.SaveChanges();

            return;
        }

        // call the url and convert the response to a number
        public int _GetValueFromUrl(string address)
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

        public int _GetPullsNumber(string address)
        {
            return _GetValueFromUrl(address + "/pulls");
        }

        public int _GetMachinesNumber(string address)
        {
            return _GetValueFromUrl(address + "/machines");
        }

        public int _GetMachinePullScore(string address, int machine, int pull)
        {
            return _GetValueFromUrl(address + "/" + machine.ToString() + "/" + pull.ToString());
        }

        public void _SetPlayerPlayState(bool state)
        {
            HttpRuntime.Cache.Insert("PlayerPlaying", state);
        }

        public void _SetPlayerPlayProgress(int progess)
        {

            HttpRuntime.Cache.Insert("PlayerProgress", progess);
        }

        public bool _GetPlayerPlayState()
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

            return (int) retVal;
        }

        // GET: /Play/Log

        public ActionResult Log()
        {
            List<Log> logs = _GetLogsFromDb();

            return PartialView("_LogPartial", logs);
        }

        protected override void OnException(ExceptionContext filterContext)
        {
            //If the exeption is already handled we do nothing
            if (filterContext.ExceptionHandled)
                return;
            else
                _AddLogToDb("Error: " + filterContext.Exception.ToString(), false);

            filterContext.ExceptionHandled = true;
        }

    }
}

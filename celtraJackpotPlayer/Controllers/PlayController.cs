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
            _SetPlayerPlayProgress(0);
            _SetPlayerPlayState(true);

            string address = "http://celtra-jackpot.com/1";
            bool isGameNew = false;


            Game gameData = _InitializeGameData(address);

            gameData.NumOfPlays++;

            int score = 0;
            int progress = 0;
            
            // if negative

            for (int pullNumber = 1; pullNumber <= gameData.Pulls; pullNumber++)
            {
                score += _GetMachinePullScore(address, 1, pullNumber);

                if (pullNumber % (gameData.Pulls / 100) == 0)
                    _SetPlayerPlayProgress(++progress);
            }


            gameData.Score[gameData.NumOfPlays - 1] = score;


            gameData = _UpdateGameObjectForDbSave(gameData);
            if (gameData.NumOfPlays > 1)
                _SaveGameToDb(gameData);
            else
                _AddGameToDb(gameData);

            _SetPlayerPlayState(false);

            

            return Content(score.ToString(), "text/plain");

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
                // TODO: log this error
                string error = ex.ToString();
            }

            if (response.Equals("ERR") || response.Equals(""))
            {
                // TODO: log this error
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

        // GET: /Play/PlayerPlaying
        public bool PlayerPlaying()
        {
            bool? retVal = HttpRuntime.Cache.Get("PlayerPlaying") as bool?;

            if (retVal == null) return false;

            return (bool) retVal;
        }

        // GET: /Play/PlayerProgress
        public int PlayerProgress()
        {
            int? retVal = HttpRuntime.Cache.Get("PlayerProgress") as int?;

            if (retVal == null) return 0;

            return (int) retVal;
        }

    }
}

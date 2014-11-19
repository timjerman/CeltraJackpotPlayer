using System;
using System.Collections.Generic;
using System.Linq;
using System.Web;
using celtraJackpotPlayer.Models;

namespace celtraJackpotPlayer.Utilities
{
    public class DataManipulation
    {

        // convert all arrays in the game objects to strings so that they can be stored in the database
        public static Game _UpdateGameObjectForDbSave(Game gameData)
        {
            gameData.ScoreStr = _IntArrayToString(gameData.Score);
            gameData.SectionsStr = _IntArrayToString(gameData.Sections);
            gameData.ProbabilitiesStr = _IntMatrixToString(gameData.Probabilities);
            gameData.SectionsScoreStr = _IntMatrixToString(gameData.SectionsScore);
            gameData.SectionsCountStr = _IntMatrixToString(gameData.SectionsCount);
            gameData.SectionsScore50Str = _IntMatrixToString(gameData.SectionsScore50);
            gameData.SectionsCount50Str = _IntMatrixToString(gameData.SectionsCount50);
            gameData.SectionsScore100Str = _IntMatrixToString(gameData.SectionsScore100);
            gameData.SectionsCount100Str = _IntMatrixToString(gameData.SectionsCount100);

            return gameData;
        }

        // convert all strings (needed for storing in the db) in the game object to arrays 
        public static Game _PrepareGameObjectForComputation(Game gameData)
        {
            gameData.Score = _StringToIntArray(gameData.ScoreStr);
            gameData.Sections = _StringToIntArray(gameData.SectionsStr);
            gameData.Probabilities = _StringToIntMatrix(gameData.ProbabilitiesStr);
            gameData.SectionsScore = _StringToIntMatrix(gameData.SectionsScoreStr);
            gameData.SectionsCount = _StringToIntMatrix(gameData.SectionsCountStr);
            gameData.SectionsScore50 = _StringToIntMatrix(gameData.SectionsScore50Str);
            gameData.SectionsCount50 = _StringToIntMatrix(gameData.SectionsCount50Str);
            gameData.SectionsScore100 = _StringToIntMatrix(gameData.SectionsScore100Str);
            gameData.SectionsCount100 = _StringToIntMatrix(gameData.SectionsCount100Str);
            return gameData;
        }


        // convert int array to string -> for storing in the database
        public static string _IntArrayToString(int[] array)
        {
            return String.Join(";", new List<int>(array).ConvertAll(i => i.ToString()).ToArray());
        }

        // extract int array from string -> for storing in the database
        public static int[] _StringToIntArray(string str)
        {
            return str.Split(';').Select(n => Convert.ToInt32(n)).ToArray();
        }

        // convert int matrix to string -> for storing in the database
        public static string _IntMatrixToString(int[,] matrix)
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
        public static int[,] _StringToIntMatrix(string str)
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

        public static string _PrepareAddress(string address)
        {
            address = address.Replace("http:\\\\", "");
            address = address.Replace("http://", "");
            address = address.Replace("www.", "");
            address = "http://www." + address;
            return address;
        }

        // sum all scores from the game with the same address
        public static int _SumOfScores(Game gameData)
        {
            int sum = 0;
            for (int i = 0; i < gameData.NumOfPlays; i++)
                sum += gameData.Score[i];

            return sum;
        }
    }
}
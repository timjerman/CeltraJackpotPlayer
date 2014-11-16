﻿using System;
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

            string address = "http://celtra-jackpot.com/4";

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
            gameData = Utilities.DataManipulation._UpdateGameObjectForDbSave(gameData);
            gameData.LastGameTime = System.DateTime.Now;
            if (gameData.NumOfPlays > 1)
                _SaveGameToDb(gameData);
            else
                _AddGameToDb(gameData);

            int resultingScore = gameData.Score[gameData.NumOfPlays - 1];
            _AddLogToDb("score: " + resultingScore.ToString() + " (sum: " + Utilities.DataManipulation._SumOfScores(gameData).ToString() + "), game: " + address, true);
            _SetPlayerPlayState(false);

            return Content(resultingScore.ToString(), "text/plain");
        }


        // ---------------------------------------------------------------------------------
        // ---------------------------- MAIN CALCULATION - BEGIN ---------------------------   -----------------------------------------------------------------------------
        // ----------------------------------------------------------------------------------

        // main method which tries to selects the machines so that the score is maximized
        private Game _Play(Game gameData)
        {
            // parameters
            byte sectionStopCriteria = 5; // when a machine gathers this score create a new section
            byte maxSectionFraction = 10; // what is the maximum possible section -> all pulls/this
            double lowProbabilityThr = 0.05; // the threshold to distinguish between two modes -> for low probabilities less confidently assign weights
            double kolSmiThr = 0.5; // threshold for the kolmogorov smirnov test for testing if the probabilities are konstant
            double partDiffThr = 0.1; //threshold for the second constant probability test
            double initFactThrToCut = 0.7;  // for constant distributions: if the second highest probability / the highest probability is greater than this factor only use the best machine
            double maxFactThrToCut = 0.95;
            double sectProbThr = 0.2; // initial threshold for thresholding low probabilities

            int score = 0;
            int progress = 0;
            bool isLowProbability = false;

            gameData.NumOfPlays++;

            // --------------------------  FIRST PLAY OF A PARTICULAR GAME   -----------------------------------------------------------------------------
            //   ----------------------------------------------------------------------------------------------------------------------------------------

            // if this is the first run of the game calculate the segments
            // the selection probability for the first run is the same for every machine
            if (gameData.NumOfPlays == 1)
            {
                // --------------------------  INITIAL MACHINE WEIGHTS   -----------------------------------------------------------------------------

                // initially all machines are pulled with the same probability
                for (int machine = 0; machine < gameData.Machines; machine++)
                {
                    gameData.Probabilities[0, machine] = 1000 / gameData.Machines;   // probabilities are converted to cummulative sum
                    if (machine > 0)
                        gameData.Probabilities[0, machine] += gameData.Probabilities[0, machine - 1];
                }

                // --------------------------   SECTION CREATION  + PULLS ON THE MACHINE  -----------------------------------------------------------------------------

                bool searhForNewSection = true;
                bool forceNewSection = false;
                int sectionIndex = 0;
                int expextedSectionIndex = 1;
                int pullFromLastNewSection = 0;
                int[] scorePerSection = new int[gameData.Machines];

                // go through all availible pulls and set the section
                for (int pull = 1; pull <= gameData.Pulls; pull++)
                {
                    pullFromLastNewSection++;
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
                        pullFromLastNewSection = 0;
                        for (int i = 0; i < scorePerSection.Length; i++) //reset
                            scorePerSection[i] = 0;
                        searhForNewSection = true;
                        if (sectionIndex > (gameData.Sections.Length - 1)) //resize all arays if they are too small
                            gameData = _ResizeGameDataArrays(gameData, sectionIndex + 50);
                    }

                    // lets make the sections at least the size of all pulls / maxSectionFraction
                    if ((pullFromLastNewSection >= (gameData.Pulls / maxSectionFraction - 1)) && scorePerSection.Max() > sectionStopCriteria / 2)
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
                        _SetPlayerPlayProgress(++progress - 1);
                }  // end of for (int pull = 1; pull <= gameData.Pulls; pull++)

                _SetPlayerPlayProgress(99);

                if (gameData.Sections[sectionIndex - 1] != gameData.Pulls)
                    gameData.Sections[sectionIndex] = gameData.Pulls;
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
                    gameData.Sections[sectionIndex - 1] = gameData.Sections[sectionIndex];
                    gameData.Sections[sectionIndex] = 0;
                    sectionIndex--;
                }

                gameData.NumOfSections = sectionIndex + 1;  // don't forget the zero index

                //resize the arrays to the section length
                _ResizeGameDataArrays(gameData, gameData.NumOfSections);

                if (((double)score) / gameData.Pulls < lowProbabilityThr)
                    isLowProbability = true;

                // --------------------------   COMPUTE WEIGHTS FOR THE NEXT PASS   -----------------------------------------------------------------------------


                // probabilities from the section scores
                double[,] sectionProbabilities = _ComputeSectionProbabilites(gameData);

                // check if section probabilities are constant
                gameData.isConstant = _isReturnProbabilityConstant(gameData, sectionProbabilities, kolSmiThr, partDiffThr);

                // average probabilities
                if (gameData.isConstant != isProbabilityConstant.NotConstant)
                    sectionProbabilities = _AverageProbabilities(sectionProbabilities, gameData);

                double[] sumPerMachine = new double[gameData.Machines];

                // normalize so that sum for same section is 1
                for (int sect = 0; sect < gameData.NumOfSections; sect++)
                {
                    double sum = 0;
                    for (int machine = 0; machine < gameData.Machines; machine++)
                        sum += sectionProbabilities[sect, machine];
                    for (int machine = 0; machine < gameData.Machines; machine++)
                    {
                        sectionProbabilities[sect, machine] = sectionProbabilities[sect, machine] / sum;
                        sumPerMachine[machine] += sectionProbabilities[sect, machine];
                    }
                }

                // if the data is found to be constant give more weight to it
                if (gameData.isConstant != isProbabilityConstant.NotConstant)
                {
                    if (gameData.Machines > 1)
                    {
                        double sumMax1 = (sumPerMachine[0] > sumPerMachine[1]) ? sumPerMachine[0] : sumPerMachine[1];
                        double sumMax2 = (sumPerMachine[0] > sumPerMachine[1]) ? sumPerMachine[1] : sumPerMachine[0];
                        int idx = (sumPerMachine[0] > sumPerMachine[1]) ? 0 : 1;

                        for (int machine = 2; machine < gameData.Machines; machine++)
                            if (sumPerMachine[machine] > sumMax1)
                            {
                                sumMax2 = sumMax1;
                                sumMax1 = sumPerMachine[machine];
                                idx = machine;
                            }

                        double factor = sumMax2 / sumMax1;

                        if (gameData.isConstant == isProbabilityConstant.HighCertainty && !isLowProbability)
                            initFactThrToCut += 0.15;

                        if (factor < initFactThrToCut)
                        {
                            for (int machine = 0; machine < gameData.Machines; machine++)
                            {
                                for (int sect = 0; sect < gameData.NumOfSections; sect++)
                                    sectionProbabilities[sect, machine] = (idx == machine) ? 1 : 0;
                            }
                        }
                    }
                }

                // for low probabilities and not constant still use the uniform distribution in the second pass
                if (isLowProbability && gameData.isConstant == isProbabilityConstant.NotConstant)
                {
                    int[,] sectionProbabilitieslp = new int[gameData.NumOfSections, gameData.Machines];

                    for (int sect = 0; sect < gameData.NumOfSections; sect++)
                        for (int machine = 0; machine < gameData.Machines; machine++)
                            sectionProbabilitieslp[sect, machine] = gameData.Probabilities[0, machine];

                    gameData.Probabilities = sectionProbabilitieslp;
                }
                else
                {
                    // convert the probabilities to cummulative sums in percents
                    for (int sect = 0; sect < gameData.NumOfSections; sect++)
                    {
                        double sum = sectionProbabilities[sect, 0];
                        gameData.Probabilities[sect, 0] = (int)Math.Round(1000 * sectionProbabilities[sect, 0]);
                        for (int machine = 1; machine < gameData.Machines; machine++)
                        {
                            sum += sectionProbabilities[sect, machine];
                            gameData.Probabilities[sect, machine] = (int)Math.Round(1000 * sum);   // probabilities are converted to cummulative sum
                        }
                    }
                }

                _SetPlayerPlayProgress(100);

            }
            else  // not the first run
            {
                // --------------------------  PLAYS OF A PARTICULAR GAME AFTER THE FIRST ONE   -----------------------------------------------------------------------------
                //   ----------------------------------------------------------------------------------------------------------------------------------------

                // -------------------------- PULLS ON THE MACHINE  -----------------------------------------------------------------------------

                int sectionIndex = 0;

                // go through all availible pulls and set the section
                for (int pull = 1; pull <= gameData.Pulls; pull++)
                {
                    byte selectedMachine = _selectMachine(gameData.Probabilities, 0);

                    int machineScore = _GetMachinePullScore(gameData.GameLocation, selectedMachine, pull);
                    if (machineScore < 0)
                        return null;

                    score += machineScore;

                    // accumulate section probabilities
                    gameData.SectionsScore[sectionIndex, selectedMachine - 1] += machineScore;
                    gameData.SectionsCount[sectionIndex, selectedMachine - 1]++;

                    // check if a new section is struct
                    if (pull == gameData.Sections[sectionIndex])
                        sectionIndex++;

                    // save progress for web display
                    if (pull % (gameData.Pulls / 100) == 0)
                        _SetPlayerPlayProgress(++progress - 1);

                }

                // --------------------------   COMPUTE WEIGHTS FOR THE NEXT PASS   -----------------------------------------------------------------------------

                if (((double)score) / gameData.Pulls < lowProbabilityThr)
                    isLowProbability = true;

                // probabilities from the section scores
                double[,] sectionProbabilities = _ComputeSectionProbabilites(gameData);

                // check if section probabilities are constant
                if (gameData.isConstant != isProbabilityConstant.HighCertainty)
                    gameData.isConstant = _isReturnProbabilityConstant(gameData, sectionProbabilities, kolSmiThr, partDiffThr);

                // average probabilities
                if (gameData.isConstant != isProbabilityConstant.NotConstant)
                    sectionProbabilities = _AverageProbabilities(sectionProbabilities, gameData);

                // weight the probabilities with a power function and cut low probabilities -> with time the best probability should remain
                for (int sect = 0; sect < gameData.NumOfSections; sect++)
                {
                    double maxVal = 0;

                    for (int machine = 0; machine < gameData.Machines; machine++)
                        if (sectionProbabilities[sect, machine] > maxVal)
                            maxVal = sectionProbabilities[sect, machine];

                    // apply a power function and threshold low probabilities
                    for (int machine = 0; machine < gameData.Machines; machine++)
                    {
                        sectionProbabilities[sect, machine] = Math.Pow(sectionProbabilities[sect, machine] / maxVal, (1 + (gameData.NumOfPlays - 2) * maxVal * 2));// with time and the max probability more confidently change the distribution
                        if (!isLowProbability)
                        {
                            if (sectionProbabilities[sect, machine] < sectProbThr)
                                sectionProbabilities[sect, machine] = 0;
                        }
                        else
                        {
                            if (sectionProbabilities[sect, machine] < sectProbThr / 2 && gameData.NumOfPlays > 3)
                                sectionProbabilities[sect, machine] = 0;
                        }
                    }
                }

                //if (sectProbThr < 0.5)
                //    sectProbThr += 0.05;


                double[] sumPerMachine = new double[gameData.Machines];

                // normalize so that sum for same section is 1
                for (int sect = 0; sect < gameData.NumOfSections; sect++)
                {
                    double sum = 0;
                    for (int machine = 0; machine < gameData.Machines; machine++)
                        sum += sectionProbabilities[sect, machine];
                    for (int machine = 0; machine < gameData.Machines; machine++)
                    {
                        sectionProbabilities[sect, machine] = sectionProbabilities[sect, machine] / sum;
                        sumPerMachine[machine] += sectionProbabilities[sect, machine];
                    }
                }

                // if the data is found to be constant give more weight to it
                if (gameData.isConstant != isProbabilityConstant.NotConstant)
                {
                    if (gameData.Machines > 1)
                    {
                        double sumMax1 = (sumPerMachine[0] > sumPerMachine[1]) ? sumPerMachine[0] : sumPerMachine[1];
                        double sumMax2 = (sumPerMachine[0] > sumPerMachine[1]) ? sumPerMachine[1] : sumPerMachine[0];
                        int idx = (sumPerMachine[0] > sumPerMachine[1]) ? 0 : 1;

                        for (int machine = 2; machine < gameData.Machines; machine++)
                            if (sumPerMachine[machine] > sumMax1)
                            {
                                sumMax2 = sumMax1;
                                sumMax1 = sumPerMachine[machine];
                                idx = machine;
                            }

                        int maxBest = 0;
                        for (int sect = 0; sect < gameData.NumOfSections; sect++)
                            if (gameData.SectionsScore[sect, idx] > maxBest)
                                maxBest = gameData.SectionsScore[sect, idx];

                        // there should be enough hits for trying to determine anything - so not to have throubles with low probabilities
                        if (maxBest > 15)
                        {

                            double factor = sumMax2 / sumMax1;

                            // for high confidence increase the speed of determining the best machine
                            int highConfidenceFactor = (gameData.isConstant == isProbabilityConstant.HighCertainty) ? 2 : 0;

                            initFactThrToCut += Math.Pow((sumMax1 / gameData.NumOfSections), 0.1) * 0.05 * (gameData.NumOfPlays + highConfidenceFactor - 1);

                            if (initFactThrToCut > maxFactThrToCut)
                                initFactThrToCut = maxFactThrToCut;

                            if (factor < initFactThrToCut)  // keep only the best
                            {
                                for (int machine = 0; machine < gameData.Machines; machine++)
                                {
                                    for (int sect = 0; sect < gameData.NumOfSections; sect++)
                                        sectionProbabilities[sect, machine] = (idx == machine) ? 1 : 0;
                                }
                            }
                            else // TODO: cut the worst
                            {
                                //stCut = stCut + 1;
                                //if stCut > floor(machines(N)/round(machines(N)/5))-1
                                //    stCut = stCut - 1;
                                //end
                                //nn(:,Iswp(1:stCut*round(machines(N)/5))) = nn(:,Iswp(1:stCut*round(machines(N)/5)))/4;
                                //nn = nn./ repmat(sum(nn,2),1,machines(N));     

                            }
                        }
                    }
                }


                for (int sect = 0; sect < gameData.NumOfSections; sect++)
                {
                    double max = 0;
                    double sum = 0;

                    for (int machine = 0; machine < gameData.Machines; machine++)
                        if (gameData.SectionsCount[sect, machine] > max && sectionProbabilities[sect, machine] != 0)
                            max = gameData.SectionsCount[sect, machine];

                    for (int machine = 0; machine < gameData.Machines; machine++)
                    {
                        if (gameData.SectionsCount[sect, machine] < max * 0.5)
                            sectionProbabilities[sect, machine] = 0;
                        sum += sectionProbabilities[sect, machine];
                    }

                    for (int machine = 0; machine < gameData.Machines; machine++)
                        sectionProbabilities[sect, machine] = sectionProbabilities[sect, machine] / sum;
                }


                // convert the probabilities to cummulative sums in percents
                for (int sect = 0; sect < gameData.NumOfSections; sect++)
                {
                    double sum = sectionProbabilities[sect, 0];
                    gameData.Probabilities[sect, 0] = (int)Math.Round(1000 * sectionProbabilities[sect, 0]);
                    for (int machine = 1; machine < gameData.Machines; machine++)
                    {
                        sum += sectionProbabilities[sect, machine];
                        gameData.Probabilities[sect, machine] = (int)Math.Round(1000 * sum);   // probabilities are converted to cummulative sum
                    }
                }

            }

            if (gameData.NumOfPlays > gameData.Score.Length)
            {
                int[] tmpArray = gameData.Score;
                Array.Resize<int>(ref tmpArray, gameData.Score.Length + 20);
                gameData.Score = tmpArray;
            }

            gameData.Score[gameData.NumOfPlays - 1] = score;

            return gameData;
        }


        // ----------------------------------------------------------------------------------
        // ---------------------------- MAIN CALCULATION - END -----------------------------   -----------------------------------------------------------------------------
        // ---------------------------------------------------------------------------------


        private double[,] _ComputeSectionProbabilites(Game gameData)
        {
            double[,] sectionProbabilities = new double[gameData.NumOfSections, gameData.Machines];

            for (int machine = 0; machine < gameData.Machines; machine++)
                for (int sect = 0; sect < gameData.NumOfSections; sect++)
                {
                    if (gameData.SectionsCount[sect, machine] > 0)
                        sectionProbabilities[sect, machine] = ((double)gameData.SectionsScore[sect, machine]) / gameData.SectionsCount[sect, machine];
                    else sectionProbabilities[sect, machine] = 0;
                }

            return sectionProbabilities;
        }


        // average probabilites based on the neighbours
        private double[,] _AverageProbabilities(double[,] sectionProbabilities, Game gameData)
        {
            double[,] averegedProbabilities = sectionProbabilities;

            for (int machine = 0; machine < gameData.Machines; machine++)
            {
                for (int sect = 1; sect < gameData.NumOfSections - 1; sect++)
                    averegedProbabilities[sect, machine] = (sectionProbabilities[sect - 1, machine] + sectionProbabilities[sect, machine] + sectionProbabilities[sect + 1, machine]) / 3;
                averegedProbabilities[0, machine] = (sectionProbabilities[0, machine] + sectionProbabilities[1, machine]) / 2;
                averegedProbabilities[gameData.NumOfSections - 1, machine] = (sectionProbabilities[gameData.NumOfSections - 1, machine] + sectionProbabilities[gameData.NumOfSections - 2, machine]) / 2;
            }

            return averegedProbabilities;
        }


        // test the probability using the kolmogorov-smirnov test
        private isProbabilityConstant _isReturnProbabilityConstant(Game gameData, double[,] sectionProb, double kolSmiThr, double partDiffThr)
        {
            double[] part1VsPart2 = new double[gameData.Machines];
            double sumKolSmi = 0;

            for (int machine = 0; machine < gameData.Machines; machine++)
            {
                double[] cumSumTest = new double[gameData.NumOfSections];
                double cumSumTheory;
                double sumPart1 = 0;
                double sumPart2 = 0;

                cumSumTest[0] = sectionProb[0, machine];
                for (int sect = 1; sect < gameData.NumOfSections; sect++)
                    cumSumTest[sect] = cumSumTest[sect - 1] + sectionProb[sect, machine];

                double maxCumSumTest = cumSumTest[gameData.NumOfSections - 1];
                double maxDiff = 0;

                for (int sect = 0; sect < gameData.NumOfSections; sect++)
                {
                    cumSumTest[sect] = cumSumTest[sect] / maxCumSumTest;
                    cumSumTheory = ((double)(sect + 1)) / gameData.NumOfSections;

                    double localDiff = Math.Abs(cumSumTest[sect] - cumSumTheory);
                    localDiff *= (localDiff * gameData.NumOfSections);

                    if (localDiff > maxDiff)
                        maxDiff = localDiff;

                    if (sect < gameData.NumOfSections / 2)
                        sumPart1 += sectionProb[sect, machine];
                    else
                        sumPart2 += sectionProb[sect, machine];
                }

                part1VsPart2[machine] = sumPart1 / sumPart2;
                sumKolSmi += Math.Sqrt(maxDiff);
            }

            if ((sumKolSmi / gameData.Machines) < kolSmiThr)
            {
                double sum = 0;

                for (int machine = 0; machine < gameData.Machines; machine++)
                    sum += (part1VsPart2[machine] - 1) * (part1VsPart2[machine] - 1);

                if ((sum / gameData.Machines) < partDiffThr)
                    return isProbabilityConstant.HighCertainty;

                return isProbabilityConstant.LowCertainty;
            }

            return isProbabilityConstant.NotConstant;
        }


        // randomly select the machine based on its probability
        private byte _selectMachine(int[,] probabilities, int section)
        {
            byte selectedMachine = 1;
            int rndNumber = rndGen.Next(1, 1001); // numbers 1-1000

            // randomly select machine based on the weights
            for (int i = 0; i < probabilities.GetLength(1) - 1; i++)
            {
                if (rndNumber > probabilities[section, i])
                    selectedMachine++;
                else break;
            }

            return selectedMachine;  // starts from 1
        }


        private Game _ResizeGameDataArrays(Game gameData, int newSize)
        {
            int[] tmpArray = gameData.Sections;
            Array.Resize<int>(ref tmpArray, newSize);
            gameData.Sections = tmpArray;

            int[,] tmpArray2D = gameData.SectionsCount;
            _Resize2DArray(ref tmpArray2D, newSize);
            gameData.SectionsCount = tmpArray2D;

            tmpArray2D = gameData.SectionsScore;
            _Resize2DArray(ref tmpArray2D, newSize);
            gameData.SectionsScore = tmpArray2D;

            tmpArray2D = gameData.Probabilities;
            _Resize2DArray(ref tmpArray2D, newSize);
            gameData.Probabilities = tmpArray2D;

            return gameData;
        }

        // resizing of a 2D array
        private void _Resize2DArray(ref int[,] array, int newSize)
        {
            int[,] tmpArray = new int[newSize, array.GetLength(1)];
            int minSize = Math.Min(newSize, array.GetLength(0));
            for (int i = 0; i < minSize; i++)
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

        // load data from database or create a new object if the entry does not exist
        private Game _InitializeGameData(string address)
        {
            address = Utilities.DataManipulation._PrepareAddress(address);

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
                gameData.isConstant = isProbabilityConstant.NotConstant;
                gameData.Score = new int[100];
                gameData.Sections = new int[100];
                gameData.Probabilities = new int[100, gameData.Machines];
                gameData.SectionsScore = new int[100, gameData.Machines];
                gameData.SectionsCount = new int[100, gameData.Machines];
            }
            else gameData = Utilities.DataManipulation._PrepareGameObjectForComputation(gameData);

            return gameData;
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

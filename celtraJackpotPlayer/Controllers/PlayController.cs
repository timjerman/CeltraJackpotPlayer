using System;
using System.Collections.Generic;
using System.Data;
using System.IO;
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

        public object Start(string address)
        {
            if (address == null || address == "")
            {
                _SetPlayerPlayState(false);
                _AddLogToDb("A non valid address was specified!", false);
                return Content("ERR", "text/plain");
            }

            _SetPlayerPlayState(true);
            _SetPlayerPlayProgress(0);

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
            double kolSmiThr = 0.49; // threshold for the kolmogorov smirnov test for testing if the probabilities are konstant
            double partDiffThr = 0.09; //threshold for the second constant probability test
            double initFactThrToCut = 0.7;  // for constant distributions: if the second highest probability / the highest probability is greater than this factor only use the best machine
            double maxFactThrToCut = 0.95;
            double sectProbThr = 0.25; // initial threshold for thresholding low probabilities

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
                int sectionIndex50 = 1;
                int sectionIndex100 = 1;
                bool sectionDetermined = false;

                // go through all availible pulls and set the section
                for (int pull = 1; pull <= gameData.Pulls; pull++)
                {
                    pullFromLastNewSection++;
                    byte selectedMachine = _selectMachine(gameData.Probabilities, 0); // the section is zero as in the first pass all machines for all sections have same probability

                    int machineScore = _GetMachinePullScore(gameData.GameLocation, selectedMachine, pull);
                    if (machineScore < 0)
                        return null;

                    score += machineScore;
                    scorePerSection[selectedMachine - 1] += machineScore;

                    // accumulate section probabilities
                    gameData.SectionsScore[sectionIndex, selectedMachine - 1] += machineScore;
                    gameData.SectionsCount[sectionIndex, selectedMachine - 1]++;

                    gameData.SectionsScore50[sectionIndex50 - 1, selectedMachine - 1] += machineScore;
                    gameData.SectionsCount50[sectionIndex50 - 1, selectedMachine - 1]++;
                    gameData.SectionsScore100[sectionIndex100 - 1, selectedMachine - 1] += machineScore;
                    gameData.SectionsCount100[sectionIndex100 - 1, selectedMachine - 1]++;

                    // check if a new section is found
                    if (((scorePerSection.Max() >= sectionStopCriteria) || forceNewSection) && searhForNewSection)
                    {
                        gameData.Sections[sectionIndex] = ((int)Math.Ceiling(pull / 10.0)) * 10; // round the new section
                        searhForNewSection = false;
                        forceNewSection = false;
                        sectionDetermined = true;
                    }

                    // when the section border is hit progress to the next one
                    if (pull == gameData.Sections[sectionIndex])
                    {
                        sectionIndex++;
                        sectionDetermined = false;
                        pullFromLastNewSection = 0;
                        for (int i = 0; i < scorePerSection.Length; i++) //reset
                            scorePerSection[i] = 0;
                        searhForNewSection = true;
                        if (sectionIndex > (gameData.Sections.Length - 1)) //resize all arays if they are too small
                            gameData = _ResizeGameDataArrays(gameData, sectionIndex + 50);
                    }

                    // lets make the sections at least the size of all pulls / maxSectionFraction
                    if ((pullFromLastNewSection >= (gameData.Pulls / maxSectionFraction - 1)) && scorePerSection.Max() > sectionStopCriteria / 2 && !sectionDetermined)
                    {
                        if (sectionIndex < expextedSectionIndex)
                        {
                            forceNewSection = true;
                            searhForNewSection = true;
                        }
                        expextedSectionIndex++;
                    }

                    // check if a new predefined section is struct
                    if (pull == (sectionIndex50 * gameData.Pulls / gameData.SectionsScore50.GetLength(0)))
                        sectionIndex50++;
                    if (pull == (sectionIndex100 * gameData.Pulls / gameData.SectionsScore100.GetLength(0)))
                        sectionIndex100++;

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
                if (gameData.isConstant != isProbabilityConstant.NotConstant || gameData.NumOfSections >= 50)
                    sectionProbabilities = _AverageProbabilities(sectionProbabilities, gameData);

                double[] sumPerMachine = new double[gameData.Machines];

                for (int sect = 0; sect < gameData.NumOfSections; sect++)
                    for (int machine = 0; machine < gameData.Machines; machine++)
                        sumPerMachine[machine] += sectionProbabilities[sect, machine];

                // if the data is found to be constant give more weight to it
                if (gameData.isConstant != isProbabilityConstant.NotConstant && gameData.Machines > 1)
                {

                    // compute sum for each machine
                    for (int sect = 0; sect < gameData.NumOfSections; sect++)
                    {
                        for (int machine = 0; machine < gameData.Machines; machine++)
                            sumPerMachine[machine] += sectionProbabilities[sect, machine];
                    }

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
                        if (!isLowProbability || factor < initFactThrToCut * 0.8)
                        {
                            for (int machine = 0; machine < gameData.Machines; machine++)
                            {
                                for (int sect = 0; sect < gameData.NumOfSections; sect++)
                                    sectionProbabilities[sect, machine] = (idx == machine) ? 1 : 0;
                            }
                        }
                        else
                        {
                            for (int sect = 0; sect < gameData.NumOfSections; sect++)
                            {
                                double sum = 0;
                                for (int machine = 0; machine < gameData.Machines; machine++)
                                {
                                    sectionProbabilities[sect, machine] = (idx == machine) ? sectionProbabilities[sect, machine] : 0.75 * sectionProbabilities[sect, machine];
                                    sectionProbabilities[sect, machine] += 0.25 * sumPerMachine[machine];
                                    sum += sectionProbabilities[sect, machine];
                                }
                                for (int machine = 0; machine < gameData.Machines; machine++)
                                    sectionProbabilities[sect, machine] /= sum;
                            }
                        }
                    }
                }

                // for low probabilities and not constant still use the uniform distribution in the second pass
                if (isLowProbability && gameData.isConstant == isProbabilityConstant.NotConstant && gameData.Machines > 5)
                {
                    int[,] sectionProbabilitieslp = new int[gameData.NumOfSections, gameData.Machines];

                    for (int sect = 0; sect < gameData.NumOfSections; sect++)
                        for (int machine = 0; machine < gameData.Machines; machine++)
                            sectionProbabilitieslp[sect, machine] = gameData.Probabilities[0, machine];

                    gameData.Probabilities = sectionProbabilitieslp;
                }
                else
                {

                    if (gameData.isConstant == isProbabilityConstant.NotConstant) // for non-constant probabilities average -> just to be sure that we don't act to quickly 
                    {
                        double globalProbability = 0;

                        for (int sect = 0; sect < gameData.NumOfSections; sect++)
                            for (int machine = 0; machine < gameData.Machines; machine++)
                                globalProbability += sectionProbabilities[sect, machine];

                        globalProbability /= (gameData.NumOfSections * gameData.Machines);

                        for (int sect = 0; sect < gameData.NumOfSections; sect++)
                            for (int machine = 0; machine < gameData.Machines; machine++)
                                sectionProbabilities[sect, machine] += globalProbability * 0.4;
                    }

                    // normalize so that sum fos each section is 1
                    for (int sect = 0; sect < gameData.NumOfSections; sect++)
                    {
                        double sum = 0;
                        for (int machine = 0; machine < gameData.Machines; machine++)
                            sum += sectionProbabilities[sect, machine];

                        if (sum == 0)
                            sum = 1;

                        for (int machine = 0; machine < gameData.Machines; machine++)
                            sectionProbabilities[sect, machine] /= sum;
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

                _SetPlayerPlayProgress(100);

            }
            else  // not the first run
            {
                // --------------------------  PLAYS OF A PARTICULAR GAME AFTER THE FIRST ONE   -----------------------------------------------------------------------------
                //   ----------------------------------------------------------------------------------------------------------------------------------------

                // -------------------------- PULLS ON THE MACHINE  -----------------------------------------------------------------------------

                int sectionIndex = 0;
                int sectionIndex50 = 1;
                int sectionIndex100 = 1;

                // go through all availible pulls and set the section
                for (int pull = 1; pull <= gameData.Pulls; pull++)
                {
                    byte selectedMachine = _selectMachine(gameData.Probabilities, sectionIndex);

                    int machineScore = _GetMachinePullScore(gameData.GameLocation, selectedMachine, pull);
                    if (machineScore < 0)
                        return null;

                    score += machineScore;

                    // accumulate section probabilities
                    gameData.SectionsScore[sectionIndex, selectedMachine - 1] += machineScore;
                    gameData.SectionsCount[sectionIndex, selectedMachine - 1]++;

                    gameData.SectionsScore50[sectionIndex50 - 1, selectedMachine - 1] += machineScore;
                    gameData.SectionsCount50[sectionIndex50 - 1, selectedMachine - 1]++;
                    gameData.SectionsScore100[sectionIndex100 - 1, selectedMachine - 1] += machineScore;
                    gameData.SectionsCount100[sectionIndex100 - 1, selectedMachine - 1]++;

                    // check if a new section is struct
                    if (pull == gameData.Sections[sectionIndex])
                        sectionIndex++;

                    // check if a new predefined section is struct
                    if (pull == (sectionIndex50 * gameData.Pulls / gameData.SectionsScore50.GetLength(0)))
                        sectionIndex50++;
                    if (pull == (sectionIndex100 * gameData.Pulls / gameData.SectionsScore100.GetLength(0)))
                        sectionIndex100++;

                    // save progress for web display
                    if (pull % (gameData.Pulls / 100) == 0)
                        _SetPlayerPlayProgress(++progress - 1);

                }

                // --------------------------   CHECK IF THE DETAILED SECTIONS CAN BE USED   ----------------------------------------------------------------

                // check if the detailed sections are ready to replace the custom one
                // there needs to be enough hits in the new sections to replace the old one
                if (gameData.isConstant == isProbabilityConstant.NotConstant)
                {
                    bool justChanged = false;

                    if (gameData.SectionStatus == sectionsInUse.Custom && gameData.NumOfSections <= gameData.SectionsScore50.GetLength(0) + 10)
                    {
                        int numMax = 0;
                        for (int sect = 0; sect < gameData.SectionsScore50.GetLength(0); sect++)
                        {
                            int max = 0;
                            for (int machine = 0; machine < gameData.Machines; machine++)
                                if (gameData.SectionsScore50[sect, machine] > max)
                                    max = gameData.SectionsScore50[sect, machine];

                            if (max > 4 * ((double)score) / gameData.SectionsScore50.GetLength(0) * Math.Pow((double)score / gameData.Pulls, 0.15))
                                numMax++;
                        }

                        // if at least half of the new sections have enough hits replace old sections
                        if (numMax > gameData.SectionsScore50.GetLength(0) / 2)
                        {
                            int[] newSections = new int[gameData.SectionsScore50.GetLength(0)];
                            for (int i = 0; i < newSections.Length; i++)
                                newSections[i] = (i + 1) * gameData.Pulls / newSections.Length;

                            gameData.Sections = newSections;
                            gameData.NumOfSections = newSections.Length;
                            gameData.SectionsCount = gameData.SectionsCount50;
                            gameData.SectionsScore = gameData.SectionsScore50;
                            gameData.Probabilities = new int[gameData.NumOfSections, gameData.Machines];

                            gameData.SectionStatus = sectionsInUse.Detailed;

                            justChanged = true;
                        }
                    }

                    if (gameData.SectionStatus != sectionsInUse.VeryDetailed && gameData.NumOfSections <= gameData.SectionsScore100.GetLength(0) + 10 && !justChanged)
                    {
                        int numMax = 0;
                        for (int sect = 0; sect < gameData.SectionsScore100.GetLength(0); sect++)
                        {
                            int max = 0;
                            for (int machine = 0; machine < gameData.Machines; machine++)
                                if (gameData.SectionsScore100[sect, machine] > max)
                                    max = gameData.SectionsScore100[sect, machine];

                            if (max > 5.5 * ((double)score) / gameData.SectionsScore50.GetLength(0) * Math.Pow((double)score / gameData.Pulls, 0.15))
                                numMax++;
                        }

                        // if at least half of the new sections have enough hits replace old sections
                        if (numMax > gameData.SectionsScore100.GetLength(0) / 2)
                        {
                            int[] newSections = new int[gameData.SectionsScore100.GetLength(0)];
                            for (int i = 0; i < newSections.Length; i++)
                                newSections[i] = (i + 1) * gameData.Pulls / newSections.Length;

                            gameData.Sections = newSections;
                            gameData.NumOfSections = newSections.Length;
                            gameData.SectionsCount = gameData.SectionsCount100;
                            gameData.SectionsScore = gameData.SectionsScore100;
                            gameData.Probabilities = new int[gameData.NumOfSections, gameData.Machines];

                            gameData.SectionStatus = sectionsInUse.VeryDetailed;
                        }
                    }
                }



                // --------------------------   COMPUTE WEIGHTS FOR THE NEXT PASS   -----------------------------------------------------------------------------

                if (((double)score) / gameData.Pulls < lowProbabilityThr)
                    isLowProbability = true;

                // probabilities from the section scores
                double[,] sectionProbabilities = _ComputeSectionProbabilites(gameData);

                // check if section probabilities are constant
                if (gameData.isConstant != isProbabilityConstant.HighCertainty)
                    gameData.isConstant = _isReturnProbabilityConstant(gameData, sectionProbabilities, kolSmiThr, partDiffThr);

                // average probabilities for non-constant distributions if there is enough sections
                // this smooths any local anomalies
                if (gameData.isConstant != isProbabilityConstant.NotConstant || gameData.NumOfSections >= 30)
                    sectionProbabilities = _AverageProbabilities(sectionProbabilities, gameData);

                // remove machines that have a small count compared to the best
                // for constant probabilities take into account also the global ones
                // differentiate between constant and non-constant probabilities
                if (gameData.isConstant == isProbabilityConstant.NotConstant) // for non-constant probabilities do it section by section
                {
                    if (gameData.NumOfPlays > 3) // because of the variability don't start this too soon
                    {
                        for (int sect = 0; sect < gameData.NumOfSections; sect++)
                        {
                            double max = 0;
                            int maxCount = 0;

                            for (int machine = 0; machine < gameData.Machines; machine++)
                            {
                                if (gameData.SectionsCount[sect, machine] > maxCount && sectionProbabilities[sect, machine] != 0)
                                {
                                    maxCount = gameData.SectionsCount[sect, machine];
                                    max = sectionProbabilities[sect, machine];
                                }
                            }

                            for (int machine = 0; machine < gameData.Machines; machine++)
                            {
                                if (gameData.SectionsCount[sect, machine] < maxCount * 0.5 && sectionProbabilities[sect, machine] < max * 0.85)
                                    sectionProbabilities[sect, machine] = 0;
                            }
                        }
                    }
                }
                else // for constant probabilities do it by machine 
                {
                    int[] sumCount = new int[gameData.Machines];
                    double[] machineProb = new double[gameData.Machines];

                    for (int sect = 0; sect < gameData.NumOfSections; sect++)
                    {
                        for (int machine = 0; machine < gameData.Machines; machine++)
                        {
                            if (sect == 0)
                            {
                                sumCount[machine] = 0;
                                machineProb[machine] = 0;
                            }
                            sumCount[machine] += gameData.SectionsCount[sect, machine];
                            machineProb[machine] += (double)gameData.SectionsScore[sect, machine] / gameData.SectionsCount[sect, machine] / gameData.NumOfSections;
                        }
                    }

                    int maxCount = sumCount.Max();
                    double maxProb = machineProb.Max();

                    for (int sect = 0; sect < gameData.NumOfSections; sect++)
                    {
                        for (int machine = 0; machine < gameData.Machines; machine++)
                        {
                            if (sumCount[machine] < maxCount * 0.5 && machineProb[machine] < maxProb * 0.85 && gameData.NumOfPlays > 3)
                                sectionProbabilities[sect, machine] = 0;
                        }
                    }
                }

                // weight the probabilities with a power function and cut low probabilities -> with time the best probability should remain
                for (int sect = 0; sect < gameData.NumOfSections; sect++)
                {
                    double maxVal = 0;

                    for (int machine = 0; machine < gameData.Machines; machine++)
                        if (sectionProbabilities[sect, machine] > maxVal)
                            maxVal = sectionProbabilities[sect, machine];

                    //double dynSectProbThr = sectProbThr * (1 + (gameData.NumOfPlays - 2) * Math.Sqrt(maxVal));
                    double funcPower = (1 + (gameData.NumOfPlays - 2) * Math.Pow(maxVal, 0.2) * 2);
                    if (funcPower > 10) funcPower = 10;

                    // apply a power function and threshold low probabilities
                    for (int machine = 0; machine < gameData.Machines; machine++)
                    {
                        sectionProbabilities[sect, machine] = Math.Pow(sectionProbabilities[sect, machine] / maxVal, funcPower);// with time and the max probability more confidently change the distribution
                        if (!isLowProbability)
                        {
                            if (sectionProbabilities[sect, machine] < sectProbThr)
                                sectionProbabilities[sect, machine] = 0;
                        }
                        else
                        {
                            if (sectionProbabilities[sect, machine] < sectProbThr * 0.5 && gameData.NumOfPlays > 3)
                                sectionProbabilities[sect, machine] = 0;
                        }
                    }
                }

                double[] sumPerMachine = new double[gameData.Machines];
                double[] sumPerMachineTrans = new double[gameData.Machines];

                for (int sect = 0; sect < gameData.NumOfSections; sect++)
                    for (int machine = 0; machine < gameData.Machines; machine++)
                    {
                        sumPerMachine[machine] += (double)gameData.SectionsScore[sect, machine] / gameData.SectionsCount[sect, machine];
                        sumPerMachineTrans[machine] += sectionProbabilities[sect, machine];
                    }


                if (gameData.isConstant != isProbabilityConstant.NotConstant && gameData.NumOfPlays % 8 == 0) // if it goes into a weird mode this can help it out
                {
                    double maxSPM = sumPerMachineTrans.Max();

                    for (int machine = 0; machine < gameData.Machines; machine++)
                        sumPerMachine[machine] *= sumPerMachineTrans[machine] / maxSPM;
                }

                // if the data is found to be constant give more weight to it
                if (gameData.isConstant != isProbabilityConstant.NotConstant)
                {
                    if (gameData.Machines > 1)
                    {
                        double sumMax1 = (sumPerMachine[0] > sumPerMachine[1]) ? sumPerMachine[0] : sumPerMachine[1];
                        double sumMax2 = (sumPerMachine[0] > sumPerMachine[1]) ? sumPerMachine[1] : sumPerMachine[0];
                        int idx = (sumPerMachine[0] > sumPerMachine[1]) ? 0 : 1;
                        int idx2 = (sumPerMachine[0] > sumPerMachine[1]) ? 1 : 0;

                        for (int machine = 2; machine < gameData.Machines; machine++)
                        {
                            if (sumPerMachine[machine] > sumMax1)
                            {
                                sumMax2 = sumMax1;
                                sumMax1 = sumPerMachine[machine];
                                idx2 = idx;
                                idx = machine;
                            }
                            if (sumPerMachine[machine] > sumMax2 && sumPerMachine[machine] < sumMax1)
                            {
                                sumMax2 = sumPerMachine[machine];
                                idx2 = machine;
                            }
                        }

                        int maxBest = 0;
                        for (int sect = 0; sect < gameData.NumOfSections; sect++)
                            if (gameData.SectionsScore[sect, idx] > maxBest)
                                maxBest = gameData.SectionsScore[sect, idx];

                        // there should be enough hits for trying to determine anything - so not to have throubles with low probabilities
                        if ((maxBest > 10 || !isLowProbability) && (!(isLowProbability && gameData.NumOfPlays < 3) || (sumMax2 / sumMax1) < initFactThrToCut * 0.8))
                        {
                            double factor = sumMax2 / sumMax1;

                            // for high confidence increase the speed of determining the best machine
                            int highConfidenceFactor = (gameData.isConstant == isProbabilityConstant.HighCertainty) ? 2 : 0;

                            initFactThrToCut += Math.Pow(sumMax1 / gameData.NumOfSections, 0.1) * 0.05 * (gameData.NumOfPlays + highConfidenceFactor - 1);

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
                            else
                            {
                                for (int sect = 0; sect < gameData.NumOfSections; sect++)
                                {
                                    sectionProbabilities[sect, idx] += 0.25;
                                    sectionProbabilities[sect, idx2] += 0.2;
                                }
                            }

                            // remove machines that have a very small sum
                            for (int sect = 0; sect < gameData.NumOfSections; sect++)
                                for (int machine = 0; machine < gameData.Machines; machine++)
                                    if (sumPerMachine[machine] < sumMax1 * 0.5)
                                        sectionProbabilities[sect, machine] = 0;
                        }
                    }
                }

                // normalize so that sum for same section is 1
                for (int sect = 0; sect < gameData.NumOfSections; sect++)
                {
                    double sum = 0;
                    for (int machine = 0; machine < gameData.Machines; machine++)
                        sum += sectionProbabilities[sect, machine];

                    if (sum == 0)
                        sum = 1;

                    for (int machine = 0; machine < gameData.Machines; machine++)
                        sectionProbabilities[sect, machine] = sectionProbabilities[sect, machine] / sum;
                }

                // sometimes perturbe the probabilities just a little bit
                if (gameData.NumOfPlays % 10 == 0 && gameData.isConstant == isProbabilityConstant.NotConstant)
                {
                    int idxc = 0;
                    for (int sect = 0; sect < gameData.NumOfSections; sect++)
                    {
                        int idx = 0;
                        double max = 0;
                        for (int machine = 0; machine < gameData.Machines; machine++)
                        {
                            if (max < sectionProbabilities[sect, machine])
                            {
                                max = sectionProbabilities[sect, machine];
                                idx = machine;
                            }
                        }

                        if (sect > 0 && idx != idxc)
                        {
                            for (int machine = 0; machine < gameData.Machines; machine++)
                            {
                                sectionProbabilities[sect, machine] += 0.03;
                                sectionProbabilities[sect, machine] /= (1 + 0.03 * gameData.Machines);
                            }
                        }

                        idxc = idx;
                    }
                }

                if (score < 0.9 * gameData.Score[0] && gameData.NumOfPlays % 3 == 0 && gameData.NumOfPlays > 5) // if the score didn't improve from the beginning set an uniform distribution
                {
                    for (int sect = 0; sect < gameData.NumOfSections; sect++)
                        for (int machine = 0; machine < gameData.Machines; machine++)
                            sectionProbabilities[sect, machine] = 1.0 / gameData.Machines;
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
                    if (sum == 0)
                        gameData.Probabilities[sect, gameData.Machines - 1] = 1000;
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
            if (gameData.NumOfSections < 5)
                return isProbabilityConstant.NotConstant;

            double[] part1VsPart2 = new double[gameData.Machines];
            double sumKolSmi = 0;

            double[] sectionsDiff = new double[gameData.NumOfSections];

            sectionsDiff[0] = gameData.NumOfSections * ((double)gameData.Sections[0]) / gameData.Pulls;
            for (int sect = 1; sect < gameData.NumOfSections; sect++)
                sectionsDiff[sect] = gameData.NumOfSections * ((double)(gameData.Sections[sect] - gameData.Sections[sect - 1])) / gameData.Pulls;

            double meanSectionDiff = sectionsDiff.Average();

            for (int sect = 0; sect < gameData.NumOfSections; sect++)
                if (Math.Abs(sectionsDiff[sect] - meanSectionDiff) > 0.3 * meanSectionDiff)
                    sectionsDiff[sect] = meanSectionDiff;

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

                    double localDiff = Math.Abs(cumSumTest[sect] - cumSumTheory) * sectionsDiff[sect];
                    localDiff *= (localDiff * gameData.NumOfSections);

                    if (localDiff > maxDiff)
                        maxDiff = localDiff;

                    if (gameData.Sections[sect] < gameData.Pulls / 2)
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

                gameData.SectionsScore50 = new int[50, gameData.Machines];
                gameData.SectionsCount50 = new int[50, gameData.Machines];
                gameData.SectionsScore100 = new int[100, gameData.Machines];
                gameData.SectionsCount100 = new int[100, gameData.Machines];
                gameData.SectionStatus = sectionsInUse.Custom;
            }
            else gameData = Utilities.DataManipulation._PrepareGameObjectForComputation(gameData);

            return gameData;
        }

        // retrieve all logs from the database
        private List<Log> _GetLogsFromDb()
        {
            var db = new GameContext();
            List<Log> returnList = db.Logs.OrderByDescending(x => x.LogID).Take(100).ToList();

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
                //using (WebClient client = new WebClient())
                //    response = client.DownloadString(address);
                var request = (HttpWebRequest)WebRequest.Create(address);
                request.Timeout = 10000;
                using (var stream = request.GetResponse().GetResponseStream())
                using (var reader = new StreamReader(stream))
                {
                    response = reader.ReadToEnd();
                }
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

        //
        // GET: /Play/Delete

        public bool Delete(string address)
        {
            if (address == null || address == "")
                return false;

            address = Utilities.DataManipulation._PrepareAddress(address);

            var db = new GameContext();
            Game gameData = db.Games.SingleOrDefault(game => game.GameLocation == address);
            if (gameData != null)
            {
                db.Games.Remove(gameData);
                db.SaveChanges();
            }

            return (gameData != null);
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

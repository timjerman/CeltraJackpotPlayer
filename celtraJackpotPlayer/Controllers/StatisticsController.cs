using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Net;
using System.Web;
using System.Web.Mvc;
using celtraJackpotPlayer.Models;
using DotNet.Highcharts;
using DotNet.Highcharts.Enums;
using DotNet.Highcharts.Helpers;
using DotNet.Highcharts.Options;

namespace celtraJackpotPlayer.Controllers
{
    public class StatisticsController : Controller
    {
        private static Random rndGen = new Random();

        // retrieve model from database by address
        private Game _GetGameFromDb(string address)
        {
            address = Utilities.DataManipulation._PrepareAddress(address);
            var db = new GameContext();
            return db.Games.SingleOrDefault(game => game.GameLocation == address);
        }

        // retrieve all games from the database
        private List<Game> _GetGamesFromDb()
        {
            var db = new GameContext();
            List<Game> returnList = db.Games.OrderByDescending(x => x.LastGameTime).ToList();

            return returnList;
        }


        // create statistics charts
        public ActionResult GameCharts(string address)
        {
            if (!Request.IsAjaxRequest())
                return RedirectToAction("Index", "Home");

            Game gameData = _GetGameFromDb(address);

            if (gameData == null)
                return new HttpStatusCodeResult(HttpStatusCode.BadRequest);

            gameData = Utilities.DataManipulation._PrepareGameObjectForComputation(gameData);

            // convert back from cummulative sum
            for (int machine = gameData.Machines - 1; machine > 0; machine--)
                for (int sect = 0; sect < gameData.NumOfSections; sect++)
                    gameData.Probabilities[sect, machine] = gameData.Probabilities[sect, machine] - gameData.Probabilities[sect, machine - 1];


            // -------------------------------------------------------------------------------------
            //                              PROBABILITY CHART
            // -------------------------------------------------------------------------------------

            Highcharts chartProbability = new Highcharts("chartProbability")
                .InitChart(new Chart { DefaultSeriesType = ChartTypes.Area, SpacingTop = 0, SpacingBottom = 0, Height = 300 })
                .SetTitle(new Title { Text = "Machine Weights" })
                .SetXAxis(new XAxis { TickInterval = 1, Title = new XAxisTitle { Text = "Section" }, Min = 1, Max = gameData.NumOfSections })
                .SetYAxis(new YAxis { Title = new YAxisTitle { Text = "Machine" } })
                .SetTooltip(new Tooltip { Formatter = "function() { return 'machine: ' + this.series.name +' ('+ Highcharts.numberFormat(this.percentage, 1) +')'; }" })
                .SetCredits(new Credits { Enabled = false })
                .SetPlotOptions(new PlotOptions
                {
                    Area = new PlotOptionsArea
                    {
                        Stacking = Stackings.Percent,
                        LineColor = ColorTranslator.FromHtml("#ffffff"),
                        LineWidth = 1,
                        Marker = new PlotOptionsAreaMarker
                        {
                            LineWidth = 1,
                            LineColor = ColorTranslator.FromHtml("#ffffff")
                        }
                    }
                });

            Series[] seriesArrayProbability = new Series[gameData.Machines];
            for (int machine = 0; machine < gameData.Machines; machine++)
            {
                Data seriesData = new Data(new object[gameData.NumOfSections, 2]);
                for (int sect = 0; sect < gameData.NumOfSections; sect++)
                {
                    seriesData.DoubleArrayData[sect, 0] = sect + 1;
                    seriesData.DoubleArrayData[sect, 1] = gameData.Probabilities[sect, machine];
                }


                seriesArrayProbability[machine] = new Series { Name = (machine + 1).ToString(), Data = seriesData };
            }

            chartProbability.SetSeries(seriesArrayProbability);

            // -------------------------------------------------------------------------------------
            //                              SCORE CHART
            // -------------------------------------------------------------------------------------

            Highcharts chartScore = new Highcharts("chartScore")
                .SetOptions(new GlobalOptions { Global = new Global { UseUTC = false } })
                .InitChart(new Chart { ZoomType = ZoomTypes.X, SpacingTop = 0, SpacingBottom = 0, Height = 300 })
                .SetTitle(new Title { Text = "Game Score" })
                .SetXAxis(new XAxis { TickInterval = 1, Title = new XAxisTitle { Text = "Plays" }, Min = 1, Max = gameData.NumOfPlays })
                .SetYAxis(new YAxis
                {
                    Title = new YAxisTitle { Text = "Score" },
                    Min = 0,
                    StartOnTick = false,
                    EndOnTick = false
                })
                .SetTooltip(new Tooltip { Shared = true })
                .SetLegend(new Legend { Enabled = false })
                .SetCredits(new Credits { Enabled = false })
                .SetPlotOptions(new PlotOptions
                {
                    Area = new PlotOptionsArea
                    {
                        FillColor = new BackColorOrGradient(new Gradient
                        {
                            LinearGradient = new[] { 0, 0, 0, 300 },
                            Stops = new object[,] { { 0, Color.LightSkyBlue }, { 1, Color.RoyalBlue } }
                        }),
                        LineWidth = 1,
                        Marker = new PlotOptionsAreaMarker
                        {
                            Enabled = true,
                            States = new PlotOptionsAreaMarkerStates
                            {
                                Hover = new PlotOptionsAreaMarkerStatesHover
                                {
                                    Enabled = true,
                                    Radius = 5
                                }
                            }
                        },
                        Shadow = false,
                        States = new PlotOptionsAreaStates { Hover = new PlotOptionsAreaStatesHover { LineWidth = 1 } }
                    }
                });

            Data seriesDataScore = new Data(new object[gameData.NumOfSections, 2]);
            for (int play = 0; play < gameData.NumOfPlays; play++)
            {
                seriesDataScore.DoubleArrayData[play, 0] = play + 1;
                seriesDataScore.DoubleArrayData[play, 1] = gameData.Score[play];
            }

            chartScore.SetSeries(new Series { Type = ChartTypes.Area, Name = "Score", Data = seriesDataScore });


            // -------------------------------------------------------------------------------------
            //                              PIE CHART - MACHINE SELECTION PER SECTION
            // -------------------------------------------------------------------------------------


            Highcharts chartSelectedMachine = new Highcharts("chartSelectedMachine")
                .InitChart(new Chart { PlotBackgroundColor = null, PlotBorderWidth = null, PlotShadow = false, SpacingTop = 0, SpacingBottom = 0, Height = 300 })
                .SetTitle(new Title { Text = "Selection per Machine" })
                .SetTooltip(new Tooltip { PointFormat = "{series.name}: <b>{point.percentage:.2f}%</b>" /*, percentageDecimals: 1*/ })
                .SetCredits(new Credits { Enabled = false })
                .SetPlotOptions(new PlotOptions
                {
                    Pie = new PlotOptionsPie
                    {
                        AllowPointSelect = true,
                        Cursor = Cursors.Pointer,
                        DataLabels = new PlotOptionsPieDataLabels
                        {
                            Enabled = true,
                            Color = ColorTranslator.FromHtml("#000000"),
                            ConnectorColor = ColorTranslator.FromHtml("#000000"),
                            Formatter = "function() { return '<b>'+ this.point.name +'</b>: '+ Highcharts.numberFormat(this.percentage,2) +' %'; }"
                        }
                    }
                });


            double[] selectedMachine = new double[gameData.Machines];

            double maxProb = 0;

            for (int sect = 0; sect < gameData.NumOfSections; sect++)
            {
                int max = gameData.Probabilities[sect, 0];
                int idx = 0;
                int numSame = 0;

                if (maxProb < ((double)gameData.SectionsScore[sect, 0]) / gameData.SectionsCount[sect, 0])
                    maxProb = ((double)gameData.SectionsScore[sect, 0]) / gameData.SectionsCount[sect, 0];

                for (int machine = 1; machine < gameData.Machines; machine++)
                {
                    if (gameData.Probabilities[sect, machine] > max)
                    {
                        max = gameData.Probabilities[sect, machine];
                        idx = machine;
                        numSame = 0;
                    }
                    else if (gameData.Probabilities[sect, machine] == max)
                        numSame++;

                    if (maxProb < ((double)gameData.SectionsScore[sect, machine]) / gameData.SectionsCount[sect, machine])
                        maxProb = ((double)gameData.SectionsScore[sect, machine]) / gameData.SectionsCount[sect, machine];
                }

                if (numSame > 0)
                {
                    for (int machine = 0; machine < gameData.Machines; machine++)
                    {
                        if (gameData.Probabilities[sect, machine] == max)
                            selectedMachine[machine] += (100.0 / gameData.NumOfSections) / (numSame + 1);
                    }
                }
                else
                    selectedMachine[idx] += 100.0 / gameData.NumOfSections;
            }
            object[] dataObject = new object[gameData.Machines];

            for (int machine = 0; machine < gameData.Machines; machine++)
                dataObject[machine] = new object[] { "Machine " + ((machine + 1).ToString()), selectedMachine[machine] };

            chartSelectedMachine.SetSeries(new Series
            {
                Type = ChartTypes.Pie,
                Name = "Selected",
                Data = new Data(dataObject)
            });


            // -------------------------------------------------------------------------------------
            //                              NON CHART STATISTICS
            // -------------------------------------------------------------------------------------

            switch (gameData.isConstant)
            {
                case isProbabilityConstant.HighCertainty:
                    ViewBag.Constant = "High Certainty";
                    break;
                case isProbabilityConstant.LowCertainty:
                    ViewBag.Constant = "Low Certainty";
                    break;
                default:
                    ViewBag.Constant = "No";
                    break;
            }

            ViewBag.Score = gameData.Score[gameData.NumOfPlays - 1].ToString() + " (total: " + Utilities.DataManipulation._SumOfScores(gameData).ToString() + ")";
            ViewBag.Machines = gameData.Machines;
            ViewBag.Pulls = gameData.Pulls;
            ViewBag.GameLocation = gameData.GameLocation;
            ViewBag.NumOfPlays = gameData.NumOfPlays;
            ViewBag.LastGameTime = gameData.LastGameTime;
            ViewBag.MaxProbability = Math.Round(maxProb * 1000) / 1000;

            List<Highcharts> charts = new List<Highcharts>();
            charts.Add(chartScore);
            charts.Add(chartProbability);
            charts.Add(chartSelectedMachine);

            return PartialView("_GameStatisticsChartsPartial", charts);
        }

        // GET: /Statistics/Games

        public ActionResult Games()
        {
            List<Game> games = _GetGamesFromDb();

            return PartialView("_GamesPartial", games);
        }

        public ActionResult MachineSelection(string address)
        {

            if (!Request.IsAjaxRequest())
                return RedirectToAction("Index", "Home");

            Game gameData = _GetGameFromDb(address);

            if (gameData == null)
                return new HttpStatusCodeResult(HttpStatusCode.BadRequest);

            gameData = Utilities.DataManipulation._PrepareGameObjectForComputation(gameData);

            string selection = "";
            int sectionIndex = 0;

            for (int pull = 1; pull <= gameData.Pulls; pull++)
            {
                int rndNumber = rndGen.Next(1, 1001); // numbers 1-1000
                int selectedMachine = 1;

                // randomly select machine based on the weights
                for (int i = 0; i < gameData.Machines - 1; i++)
                {
                    if (rndNumber > gameData.Probabilities[sectionIndex, i])
                        selectedMachine++;
                    else break;
                }

                selection += selectedMachine.ToString() + " ";

                if (pull == gameData.Sections[sectionIndex])
                    sectionIndex++;
            }

            return PartialView("_SelectionPartial", selection);
        }



























    }
}

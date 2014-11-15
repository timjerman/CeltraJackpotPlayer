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
        // retrieve model from database by address
        private Game _GetGameFromDb(string address)
        {
            address = Utilities.DataManipulation._ShortenAddress(address);
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
                .InitChart(new Chart { DefaultSeriesType = ChartTypes.Area })
                .SetTitle(new Title { Text = "Machine Weights" })
                .SetXAxis(new XAxis { TickInterval = 1, Title = new XAxisTitle { Text = "Section" } })
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
                .InitChart(new Chart { ZoomType = ZoomTypes.X })
                .SetTitle(new Title { Text = "Machine Score" })
                .SetXAxis(new XAxis { TickInterval = 1, Title = new XAxisTitle { Text = "Plays" } })
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




            List<Highcharts> charts = new List<Highcharts>();
            charts.Add(chartScore);
            charts.Add(chartProbability);

            return PartialView("_GameStatisticsChartsPartial", charts);
        }

        // GET: /Statistics/Games

        public ActionResult Games()
        {
            List<Game> games = _GetGamesFromDb();

            return PartialView("_GamesPartial", games);
        }

    }
}

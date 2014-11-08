using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading;
using System.Web;
using System.Web.Mvc;

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

            int score = 0;
            int progress = 0;
            int pulls = _GetPullsNumber(address);
            int machines = _GetMachinesNumber(address);
            
            // if negative

            for (int pullNumber = 1; pullNumber <= pulls; pullNumber++)
            {
                score += _GetMachinePullScore(address, 1, pullNumber);

                if (pullNumber % (pulls/100) == 0)
                    _SetPlayerPlayProgress(++progress);
            }

            _SetPlayerPlayState(false);

            return Content(score.ToString(), "text/plain");

        }

        // call the url and convert the response to a number
        public int _GetValueFromUrl(string address)
        {
            WebClient client = new WebClient();
            string response = client.DownloadString(address);

            if (response.Equals("ERR") || response.Equals(""))
                return -1;

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

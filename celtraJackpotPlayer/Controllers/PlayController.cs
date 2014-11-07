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
        // GET: /Play/

        public ActionResult Start()
        {

            _SetPlayerPlayProgress(0);
            _SetPlayerPlayState(true);

            if (!Request.IsAjaxRequest())
                return RedirectToAction("Index", "Home");

            for (int i = 1; i <= 10; i++)
            {
                _SetPlayerPlayProgress(i * 10);
                Thread.Sleep(1000);
            }

            _SetPlayerPlayState(false);

            var httpStatus = ModelState.IsValid ? HttpStatusCode.OK : HttpStatusCode.BadRequest;
            return new HttpStatusCodeResult(httpStatus);

        }


        public void _SetPlayerPlayState(bool state)
        {

            HttpRuntime.Cache.Insert("PlayerPlaying", state);
        }


        public void _SetPlayerPlayProgress(int progess)
        {

            HttpRuntime.Cache.Insert("PlayerProgress", progess);
        }

        public bool PlayerPlaying()
        {
            bool? retVal = HttpRuntime.Cache.Get("PlayerPlaying") as bool?;

            if (retVal == null) return false;

            return (bool) retVal;
        }


        public int PlayerProgress()
        {
            int? retVal = HttpRuntime.Cache.Get("PlayerProgress") as int?;

            if (retVal == null) return 0;

            return (int) retVal;
        }

    }
}

using System;
using System.Collections.Generic;
using System.Linq;
using System.Web;
using System.Web.Mvc;
using System.Web.Routing;

namespace celtraJackpotPlayer
{
    public class RouteConfig
    {
        public static void RegisterRoutes(RouteCollection routes)
        {
            routes.IgnoreRoute("{resource}.axd/{*pathInfo}");

            routes.MapRoute(
                name: "startPlayer",
                url: "Play/Start/{*address}",
                defaults: new { controller = "Play", action = "Start", address = "" }
            );

            routes.MapRoute(
                name: "deletePlayer",
                url: "Play/Delete/{*address}",
                defaults: new { controller = "Play", action = "Delete", address = "" }
            );

            routes.MapRoute(
                name: "startPlayerShort",
                url: "Play/{*address}",
                defaults: new { controller = "Play", action = "Start", address = "" },
                constraints: new { address = "^(?:(?!Log|PlayerProgress|PlayerPlaying|Delete|Start).)*$\r?\n?" }
            );

            routes.MapRoute(
                name: "deletePlayerShort",
                url: "Delete/{*address}",
                defaults: new { controller = "Play", action = "Delete", address = "" }
            );

            routes.MapRoute(
                name: "Default",
                url: "{controller}/{action}/{id}",
                defaults: new { controller = "Home", action = "Index", id = UrlParameter.Optional }
            );
        }
    }
}
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Web;

namespace celtraJackpotPlayer.Models
{
    public class Log
    {
        public int LogID { get; set; }
        public string Message { get; set; }

        [DisplayName("Date")]
        public DateTime LogTime { get; set; }
    }
}
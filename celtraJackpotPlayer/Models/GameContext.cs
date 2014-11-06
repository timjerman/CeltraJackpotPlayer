using System.Data.Entity;

namespace celtraJackpotPlayer.Models
{
    public class GameContext : DbContext
    {
        public GameContext() : base("GameContext")
        {
        }

        public DbSet<Game> Games { get; set; }
    }
}

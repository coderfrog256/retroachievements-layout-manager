using Newtonsoft.Json;
using System.Collections.Generic;

namespace Retro_Achievement_Tracker.Models
{
    // Minimal class with just subsets until the API is stable and there's a need to migrate.
    public partial class GameInfoV2
    {
        public HashSet<long> SubsetIds { get; set; }
    }
}

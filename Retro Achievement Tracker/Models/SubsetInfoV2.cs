using Newtonsoft.Json;
using System.Collections.Generic;

namespace Retro_Achievement_Tracker.Models
{
    // Subset from the V2 game API. Mascarades as a GameInfo for Back-Compat with flows that use V1 data.
    public class SubsetInfoV2 : GameInfo
    {
        public void AddToParent(GameInfo parent)
        {
            Parent = parent.Id;
            ConsoleId = parent.ConsoleId;
            ImageTitle = parent.ImageTitle;
            ImageIngame = parent.ImageIngame;
            ImageBoxArt = parent.ImageBoxArt;
            Publisher = parent.Publisher;
            Developer = parent.Developer;
            Genre = parent.Genre;
            Released = parent.Released;
            ConsoleName = parent.ConsoleName;
            LastPlayed = parent.LastPlayed;
            parent.Children.Add(this);
        }
    }
}

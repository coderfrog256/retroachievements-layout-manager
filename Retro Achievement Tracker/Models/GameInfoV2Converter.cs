namespace Retro_Achievement_Tracker
{
    using Newtonsoft.Json.Linq;
    using Retro_Achievement_Tracker.Models;
    using System.Linq;

    public class GameInfoV2Converter
    {
        public static GameInfoV2 FromJson(string json)
        {
            GameInfoV2 gameInfo = new GameInfoV2();
            var root = JObject.Parse(json);

            gameInfo.SubsetIds = root["included"]
                .Where(row => "achievement-sets" == (string) row["type"])
                .Select(row => (string) row["id"])
                .Select(long.Parse)
                .ToHashSet();

            return gameInfo;
        }
    }
}
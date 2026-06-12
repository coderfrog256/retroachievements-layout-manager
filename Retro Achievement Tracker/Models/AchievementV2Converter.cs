namespace Retro_Achievement_Tracker
{
    using Newtonsoft.Json.Linq;
    using Retro_Achievement_Tracker.Models;
    using System;
    using System.Collections.Generic;
    using System.Linq;

    public class AchievementV2Converter
    {
        public static (List<Achievement>, string) FromPlayerJson(string json)
        {
            var root = JObject.Parse(json);

            var achievements = root["included"] is JArray included
                ? included
                    .Where(row => "achievements" == (string)row["type"])
                    .Select(row => ParseAchievement(row, true).Item2)
                    .ToDictionary(a => a.Id)
                : new Dictionary<int, Achievement>();

            if (root["data"] is JArray data)
            {
                foreach (var playerAchievement in data.Where(row => "player-achievements" == (string)row["type"]))
                {
                    AddPlayerData(playerAchievement, achievements);
                }
            }

            string next = null;
            var nextToken = root["links"]["next"];
            if (nextToken != null)
            {
                next = (string)nextToken;
            }

            return (achievements.Values.ToList(), next);
        }

        public static (Dictionary<long, SubsetInfoV2>, string) FromAchievementJson(string json)
        {
            var root = JObject.Parse(json);

            var subsets = root["included"] is JArray included
                ? included
                    .Where(row => "achievement-sets" == (string)row["type"])
                    .Select(row => ParseSubsetInfo(row))
                    .Where(row => row != null)
                    .ToDictionary(a => a.Id)
                : new Dictionary<long, SubsetInfoV2>();

            if (root["data"] is JArray data)
            {
                foreach (var row in data.Where(row => "achievements" == (string)row["type"]))
                {
                    var (subsetId, achievement) = ParseAchievement(row, false);
                    if (subsets.TryGetValue(subsetId.Value, out var subset))
                    {
                        subset.Achievements.Add(achievement);
                    }
                }
            }

            string next = null;
            var nextToken = root["links"]["next"];
            if (nextToken != null)
            {
                next = (string)nextToken;
            }

            return (subsets, next);
        }

        private static (long?, Achievement) ParseAchievement(JToken row, bool isPlayer)
        {
            var achievement = new Achievement();
            achievement.Id = int.Parse((string)row["id"]);

            var attributes = row["attributes"];
            achievement.Title = (string)attributes["title"];
            achievement.Description = (string)attributes["description"];
            achievement.Points = (int)attributes["points"];
            achievement.TrueRatio = (int)attributes["pointsWeighted"];
            achievement.BadgeUri = (string)attributes["badgeUrl"];
            achievement.DisplayOrder = (int)attributes["orderColumn"];

            return ((long?)row.SelectToken("relationships.achievementSet.data.id"), achievement);
        }

        private static void AddPlayerData(JToken row, Dictionary<int, Achievement> achievements)
        {
            var id = int.Parse((string)row["relationships"]["achievement"]["data"]["id"]);
            var achievement = achievements[id];
            if (row["attributes"]["unlockedHardcoreAt"] is JToken date)
            {
                achievement.DateEarned = DateTime.Parse((string)date);
            }
        }

        private static SubsetInfoV2 ParseSubsetInfo(JToken row)
        {
            var subsetInfo = new SubsetInfoV2();
            subsetInfo.Children = new List<GameInfo>();
            subsetInfo.Id = long.Parse((string)row["id"]);

            var attributes = row["attributes"];
            if (attributes["title"] == null)
            {
                return null;
            }

            subsetInfo.Title = (string)attributes["title"];

            if (attributes["types"] is JArray types)
            {
                foreach (var type in types)
                {
                    if ((string)type["type"] == "core")
                    {
                        return null;
                    }
                }
            }

            subsetInfo.BadgeUri = (string)attributes["badgeUrl"];
            subsetInfo.Achievements = new List<Achievement>();

            return subsetInfo;
        }
    }
}

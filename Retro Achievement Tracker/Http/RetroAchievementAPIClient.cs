using Newtonsoft.Json;
using Retro_Achievement_Tracker.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading.Tasks;

namespace Retro_Achievement_Tracker
{
    class RetroAchievementAPIClient
    {
        private static readonly HttpClient client = new HttpClient();
        private readonly string UserName;
        private readonly string ApiKey;

        public RetroAchievementAPIClient(string username, string apiKey)
        {
            UserName = username;
            ApiKey = apiKey;

            client.DefaultRequestHeaders.CacheControl = new CacheControlHeaderValue
            {
                NoCache = true
            };
        }
        public async Task<UserSummary> GetUserSummary()
        {
            HttpResponseMessage httpResponseMessage = await client.GetAsync(string.Format(Constants.RETRO_ACHIEVEMENTS_URL + Constants.RETRO_ACHIEVEMENTS_API_GET_USER, UserName, ApiKey, UserName));

            if (!httpResponseMessage.IsSuccessStatusCode)
            {
                throw new Exception("RA backend responding with errors: " + httpResponseMessage.StatusCode);
            }
            return JsonConvert.DeserializeObject<UserSummary>(await httpResponseMessage.Content.ReadAsStringAsync());
        }
        public async Task<GameInfo> GetGameInfoAndProgress(long gameId)
        {
            HttpResponseMessage httpResponseMessage = await client.GetAsync(string.Format(Constants.RETRO_ACHIEVEMENTS_URL + Constants.RETRO_ACHIEVEMENTS_API_GET_GAME, UserName, ApiKey, UserName, gameId));

            if (!httpResponseMessage.IsSuccessStatusCode)
            {
                throw new Exception("RA backend responding with errors: " + httpResponseMessage.StatusCode);
            }
            return JsonConvert.DeserializeObject<GameInfo>(await httpResponseMessage.Content.ReadAsStringAsync());
        }

        public async Task<List<Achievement>> GetUserSubsetAchievementsV2(long subsetId)
        {
            var result = new List<Achievement>();
            var next = string.Format(Constants.RETRO_ACHIEVEMENTS_V2_URL + Constants.RETRO_ACHIEVEMENTS_API_V2_GET_PLAYER_SUBET_ACHIEVEMENTS, UserName, subsetId);
            while (next != null)
            {
                using (var request = new HttpRequestMessage(HttpMethod.Get, next))
                {
                    request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.api+json"));
                    request.Headers.Add("X-API-Key", ApiKey);

                    HttpResponseMessage httpResponseMessage = await client.SendAsync(request);
                    if (!httpResponseMessage.IsSuccessStatusCode)
                    {
                        throw new Exception("RA backend responding with errors: " + httpResponseMessage.StatusCode);
                    }
                    var (data, nextLink) = AchievementV2Converter.FromPlayerJson(await httpResponseMessage.Content.ReadAsStringAsync());
                    result.AddRange(data);
                    next = nextLink;
                }
            }
            return result;
        }

        public async Task<List<SubsetInfoV2>> GetSubsetAchievementsV2(long game)
        {
            var result = new Dictionary<long, SubsetInfoV2>();
            var next = string.Format(Constants.RETRO_ACHIEVEMENTS_V2_URL + Constants.RETRO_ACHIEVEMENTS_API_V2_GET_ACHIEVEMENTS, game);
            while (next != null)
            {
                using (var request = new HttpRequestMessage(HttpMethod.Get, next))
                {
                    request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.api+json"));
                    request.Headers.Add("X-API-Key", ApiKey);

                    HttpResponseMessage httpResponseMessage = await client.SendAsync(request);
                    if (!httpResponseMessage.IsSuccessStatusCode)
                    {
                        throw new Exception("RA backend responding with errors: " + httpResponseMessage.StatusCode);
                    }
                    var (data, nextLink) = AchievementV2Converter.FromAchievementJson(await httpResponseMessage.Content.ReadAsStringAsync());
                    next = nextLink;
                    foreach (var subset in data)
                    {
                        if(result.TryGetValue(subset.Key, out var existing))
                        {
                            existing.Achievements.AddRange(subset.Value.Achievements);
                        }
                        else
                        {
                            result[subset.Key] = subset.Value;
                        }
                    }
                }
            }
            return result.Values.ToList();
        }

        public async Task<GameInfo> GetGameInfoExtended(long gameId)
        {
            HttpResponseMessage httpResponseMessage = await client.GetAsync(string.Format(Constants.RETRO_ACHIEVEMENTS_URL + Constants.RETRO_ACHIEVEMENTS_API_GET_GAME_EXTENDED, UserName, ApiKey, gameId));

            if (!httpResponseMessage.IsSuccessStatusCode)
            {
                throw new Exception("RA backend responding with errors: " + httpResponseMessage.StatusCode);
            }
            return JsonConvert.DeserializeObject<GameInfo>(await httpResponseMessage.Content.ReadAsStringAsync());
        }
        public async Task<List<GameInfo>> GetRecentlyPlayedGames(bool fetchMoreHistory)
        {
            int lookback = fetchMoreHistory ? 50 : 1;
            HttpResponseMessage httpResponseMessage = await client.GetAsync(string.Format(Constants.RETRO_ACHIEVEMENTS_URL + Constants.RETRO_ACHIEVEMENTS_API_GET_RECENTLY_PLAYED, UserName, ApiKey, UserName, lookback));

            if (!httpResponseMessage.IsSuccessStatusCode)
            {
                throw new Exception("RA backend responding with errors: " + httpResponseMessage.StatusCode);
            }
            return JsonConvert.DeserializeObject<List<GameInfo>>(await httpResponseMessage.Content.ReadAsStringAsync());
        }
        public async Task<List<Achievement>> GetRecentAchievements()
        {
            HttpResponseMessage httpResponseMessage = await client.GetAsync(string.Format(Constants.RETRO_ACHIEVEMENTS_URL + Constants.RETRO_ACHIEVEMENTS_API_GET_RECENT_ACHIEVEMENTS, UserName, ApiKey, UserName));

            if (!httpResponseMessage.IsSuccessStatusCode)
            {
                throw new Exception("RA backend responding with errors: " + httpResponseMessage.StatusCode);
            }
            return JsonConvert.DeserializeObject<List<Achievement>>(await httpResponseMessage.Content.ReadAsStringAsync());
        }
        public async Task<UserRankAndScore> GetRankAndScore()
        {
            HttpResponseMessage httpResponseMessage = await client.GetAsync(string.Format(Constants.RETRO_ACHIEVEMENTS_URL + Constants.RETRO_ACHIEVEMENTS_API_GET_RANK_AND_SCORE, UserName, ApiKey, UserName));

            if (!httpResponseMessage.IsSuccessStatusCode)
            {
                throw new Exception("RA backend responding with errors: " + httpResponseMessage.StatusCode);
            }
            return JsonConvert.DeserializeObject<UserRankAndScore>(await httpResponseMessage.Content.ReadAsStringAsync());
        }
    }
}

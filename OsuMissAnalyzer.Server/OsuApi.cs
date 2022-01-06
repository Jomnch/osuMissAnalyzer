using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;

namespace OsuMissAnalyzer.Server
{
    public class OsuApi
    {
        private string apiKeyv1;
        private readonly HttpClient webClient;
        private string clientId;
        private string clientSecret;
        private Stopwatch tokenExpiry;
        private Queue<DateTime> replayDls;
        private int tokenTime;
        private string token;
        private TimeSpan TokenTimeRemaining => TimeSpan.FromSeconds(tokenTime).Subtract(tokenExpiry.Elapsed);
        public OsuApi(HttpClient webClient, string clientId, string clientSecret, string apiKeyv1)
        {
            this.webClient = webClient;
            this.clientId = clientId;
            this.apiKeyv1 = apiKeyv1;
            this.clientSecret = clientSecret;
            tokenExpiry = new Stopwatch();
            replayDls = new Queue<DateTime>();
        }
        public async Task RefreshToken()
        {
            HttpContent postContent = new FormUrlEncodedContent(new[]
            {
                new KeyValuePair<string, string>("client_id", clientId),
                new KeyValuePair<string, string>("client_secret", clientSecret),
                new KeyValuePair<string, string>("grant_type", "client_credentials"),
                new KeyValuePair<string, string>("scope", "public"),
            });
            tokenExpiry.Restart();
            HttpResponseMessage res = await webClient.PostAsync("https://osu.ppy.sh/oauth/token", postContent);
            JToken j = JToken.Parse(await res.Content.ReadAsStringAsync());
            tokenTime = (int)j["expires_in"];
            token = (string)j["access_token"];
            if (Logger.Instance != null) Logger.Instance.UpdateLogs += () => Logger.LogAbsolute(Logging.TokenExpiry, (int)Math.Max(TokenTimeRemaining.TotalMinutes, 0));
        }
        private async Task CheckToken()
        {
            if (TokenTimeRemaining <= TimeSpan.Zero)
                await RefreshToken();
        }
        public async Task<JToken> ApiRequestv1(string endpoint, string query)
        {
            string res = await webClient.GetStringAsync($"https://osu.ppy.sh/api/{endpoint}?k={apiKeyv1}&{query}");
            return JToken.Parse(res);
        }
        public async Task<string> GetUserIdv1(string username)
        {
            Logger.Log(Logging.ApiGetUserv1);
            var result = await ApiRequestv1("get_user", $"u={username}&type=string");
            if ((result as JArray).Count == 0) throw new ArgumentException($"No user named {username}");
            return (string)result[0]["user_id"];
        }
        public async Task<string> DownloadBeatmapFromHashv1(string mapHash, string destinationFolder)
        {
            Logger.Log(Logging.ApiGetBeatmapsv1);
            JArray j = (JArray)(await ApiRequestv1("get_beatmaps", $"h={mapHash}"));
            if (j.Count > 0)
            {
                string beatmapId = (string)j[0]["beatmap_id"];
                await DownloadBeatmapFromId(beatmapId, destinationFolder);
                return beatmapId;
            }
            return null;
        }
        public async Task DownloadBeatmapFromId(string beatmapId, string destinationFolder)
        {
            Logger.Log(Logging.ApiDownloadBeatmap);
            string file = Path.Combine(destinationFolder, $"{beatmapId}.osu");
            while(!File.Exists(file))
            {
                try
                {
                    await (await webClient.GetStreamAsync($"https://osu.ppy.sh/osu/{beatmapId}")).CopyToAsync(File.Create(file));
                }
                catch (WebException e)
                {
                    await Logger.WriteLine("Exception caught in DownloadBeatmap");
                    await Logger.LogException(e, Logger.LogLevel.NORMAL);
                }
            }
        }
        public async Task<JToken> GetUserScoresv2(string userId, string type, int index, bool failedScores)
        {
            Logger.Log(Logging.ApiGetUserScoresv2);
            var req = $"users/{userId}/scores/{type}?mode=osu&include_fails={(failedScores?1:0)}&limit=1&offset={index}";
            var res = await GetApiv2(req);
            if (res is JArray arr && arr.Count > 0)
            {
                var score = arr[0];
                if ((bool)score["replay"] && !(bool)score["perfect"])
                    return score;
            }
            else
            {
                await Logger.WriteLine($"{req} failed");
                await Logger.WriteLine(res.ToString());
            }
            return null;
        }
        public async Task<JToken> GetBeatmapScoresv2(string beatmapId, int index)
        {
            Logger.Log(Logging.ApiGetBeatmapScoresv2);
            var req = $"beatmaps/{beatmapId}/scores";
            var res = await GetApiv2(req);
            if (res["scores"] is JArray arr && arr.Count > index)
            {
                var score = arr[index];
                if ((bool)score["replay"] && !(bool)score["perfect"])
                    return score;
            }
            else
            {
                await Logger.WriteLine($"{req} failed");
                await Logger.WriteLine(res.ToString());
            }
            return null;
        }
        public async Task<JToken> GetApiv2(string endpoint)
        {
            await CheckToken();
            var request = new HttpRequestMessage(HttpMethod.Get, $"https://osu.ppy.sh/api/v2/{endpoint}");
            request.Headers.Add("Authorization", $"Bearer {token}");
            var res = await webClient.SendAsync(request);
            return JToken.Parse(await res.Content.ReadAsStringAsync());
        }
        public async Task<byte[]> DownloadReplayFromId(string onlineId)
        {
            Logger.Log(Logging.ApiGetReplayv1);
            while (replayDls.Count > 0 && (DateTime.Now - replayDls.Peek()).TotalSeconds > 60) replayDls.Dequeue();
            if (replayDls.Count >= 10)
            {
                await Task.Delay(TimeSpan.FromMinutes(1).Subtract(DateTime.Now - replayDls.Peek()));
            }
            replayDls.Enqueue(DateTime.Now);
            var res = await ApiRequestv1("get_replay", $"s={onlineId}");
            return res["content"] != null? Convert.FromBase64String((string)res["content"]) : null;
        }

    }
}
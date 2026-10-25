using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using YahtzeeSim.Analysis;
using YahtzeeSim.Analysis.Data;
using YahtzeeSim.Analysis.Replay;
using YahtzeeSim.Analysis.Reports;
using YahtzeeSim.Analysis.Solver;
using YahtzeeSim.Analysis.Story;

namespace YahtzeeSim.Web.Services;

public enum StrategyStatus
{
    Ready,
    NoGames,
    Offline,
    SessionExpired,
    Failed
}

/// <summary>The outcome of building the player's play-style story: its markup when ready, plus how many games it covers.</summary>
public sealed record StrategyReport(StrategyStatus Status, string? Html = null, int Games = 0, int Skipped = 0, int Limit = 0);

/// <summary>
/// Builds the signed-in player's play-style story and badge on this device. Their recent finished Full Sheet games come
/// from <c>api/strategy</c>; the analysis is the shared rule-based <c>YahtzeeSim.Analysis</c> code. The solver table and
/// reference simulations (needed only for the tip) are precomputed files shipped with the app. The badge is sent to
/// <c>api/style</c> so it can be shown on the leaderboard.
/// </summary>
public sealed class StrategyService(HttpClient http, AccountService account)
{
    private static readonly JsonSerializerOptions WebJson = new(JsonSerializerDefaults.Web);

    private readonly SemaphoreSlim _badgeGate = new(1, 1);
    private Task<(CardSolver Solver, ReferenceData Reference)>? _assets;
    private string? _uploadedBadge;

    private sealed record Payload(string? Nickname, int Limit, List<PayloadGame> Games);

    private sealed record PayloadGame(string GameId, int Total, long PlayedAt, List<JsonElement[]> Actions);

    /// <summary>Downloads the games and builds the story. <paramref name="progress"/> receives short status lines.</summary>
    public async Task<StrategyReport> BuildAsync(Func<string, Task> progress)
    {
        await progress("Loading your games");
        var (status, payload) = await FetchAsync();
        if (status != StrategyStatus.Ready)
        {
            return new StrategyReport(status);
        }

        if (payload!.Games.Count == 0)
        {
            return new StrategyReport(StrategyStatus.NoGames, Limit: payload.Limit);
        }

        (CardSolver Solver, ReferenceData Reference) assets;
        try
        {
            assets = await (_assets ??= LoadAssetsAsync());
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or InvalidDataException)
        {
            _assets = null;
            return new StrategyReport(StrategyStatus.Offline);
        }

        await progress($"Looking at {payload.Games.Count} game{(payload.Games.Count == 1 ? "" : "s")}");
        PlayStory story;
        AnalysisResult result;
        try
        {
            result = AnalysisRunner.Run(ToExport(payload), new AnalysisOptions(), solver: assets.Solver, referenceData: assets.Reference);
            var player = result.Players.FirstOrDefault();
            if (player == null)
            {
                return new StrategyReport(StrategyStatus.NoGames, Skipped: result.Skipped.Count, Limit: payload.Limit);
            }

            story = StoryRules.Build(player);
        }
        catch (Exception ex) when (ex is FormatException or InvalidOperationException or ArgumentException or KeyNotFoundException or IndexOutOfRangeException)
        {
            return new StrategyReport(StrategyStatus.Failed);
        }

        await UploadBadgeAsync(story.Badge.Id);
        return new StrategyReport(StrategyStatus.Ready, StoryReport.Fragment(story), story.Games, result.Skipped.Count, payload.Limit);
    }

    /// <summary>
    /// Works out the badge from the player's recent games and sends it when it changed. Cheap: replay and counting only.
    /// Call after a finished Full Sheet game has uploaded. Failures are ignored; the next game or story view retries.
    /// </summary>
    public async Task RefreshBadgeAsync()
    {
        if (!await _badgeGate.WaitAsync(0))
        {
            return;
        }

        try
        {
            var (status, payload) = await FetchAsync();
            if (status != StrategyStatus.Ready || payload!.Games.Count == 0)
            {
                return;
            }

            var data = ToExport(payload);
            var games = new List<ReplayedGame>();
            foreach (var score in data.Scores)
            {
                try
                {
                    games.Add(GameReplayer.Replay(score, data.Actions.Where(a => a.GameId == score.GameId)));
                }
                catch (ReplayException)
                {
                    // Incomplete or unchecked games are left out, as in the story.
                }
            }

            if (games.Count > 0)
            {
                await UploadBadgeAsync(BadgeRules.Choose(StyleMetrics.From(games)).Badge.Id);
            }
        }
        catch (Exception ex) when (ex is FormatException or InvalidOperationException or ArgumentException or KeyNotFoundException or IndexOutOfRangeException)
        {
        }
        finally
        {
            _badgeGate.Release();
        }
    }

    private async Task UploadBadgeAsync(string badgeId)
    {
        string key = (account.UserId ?? string.Empty) + "|" + badgeId;
        if (key == _uploadedBadge)
        {
            return;
        }

        try
        {
            using var response = await http.PutAsJsonAsync("api/style", new { badge = badgeId, version = BadgeRules.Version }, WebJson);
            if (response.IsSuccessStatusCode)
            {
                _uploadedBadge = key;
            }
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
        }
    }

    private async Task<(StrategyStatus Status, Payload? Payload)> FetchAsync()
    {
        if (account.UserId == null)
        {
            return (StrategyStatus.SessionExpired, null);
        }

        try
        {
            using var response = await http.GetAsync("api/strategy");
            if (response.StatusCode == HttpStatusCode.Unauthorized)
            {
                await account.HandleSessionExpiredAsync();
                return (StrategyStatus.SessionExpired, null);
            }

            if (!response.IsSuccessStatusCode || response.Content.Headers.ContentType?.MediaType != "application/json")
            {
                return (StrategyStatus.Offline, null);
            }

            var payload = await response.Content.ReadFromJsonAsync<Payload>(WebJson);
            return payload == null ? (StrategyStatus.Failed, null) : (StrategyStatus.Ready, payload);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            return (StrategyStatus.Offline, null);
        }
    }

    private async Task<(CardSolver, ReferenceData)> LoadAssetsAsync()
    {
        var tables = DiceTables.Shared;
        var bytes = await http.GetByteArrayAsync("analysis/card-values.bin");
        var solver = CardSolver.ReadAsset(new MemoryStream(bytes), tables);
        var reference = ReferenceData.FromJson(await http.GetStringAsync("analysis/reference.json"), tables);
        return (solver, reference);
    }

    private ExportData ToExport(Payload payload)
    {
        string userId = account.UserId ?? "me";
        string nickname = account.Nickname ?? payload.Nickname ?? "You";
        var scores = new List<ScoreRow>();
        var actions = new List<ActionRow>();
        long id = 0;
        foreach (var game in payload.Games)
        {
            scores.Add(new ScoreRow(++id, nickname, userId, game.GameId, "FullSheet", game.Total, game.PlayedAt));
            foreach (var a in game.Actions)
            {
                // [seq, action, occurredAt, turn, rollNumber, dice, dieId, gameIndex, category, points]
                int seq = a[0].GetInt32();
                actions.Add(new ActionRow(
                    seq,
                    nickname,
                    userId,
                    game.GameId,
                    seq,
                    "FullSheet",
                    a[1].GetString() ?? string.Empty,
                    a[2].GetInt64(),
                    a[3].GetInt32(),
                    a[4].GetInt32(),
                    a[5].GetString() ?? string.Empty,
                    NullableInt(a[6]),
                    NullableInt(a[7]),
                    a[8].ValueKind == JsonValueKind.Null ? null : a[8].GetString(),
                    NullableInt(a[9]),
                    false));
            }
        }

        return new ExportData([], scores, actions);

        static int? NullableInt(JsonElement e) => e.ValueKind == JsonValueKind.Null ? null : e.GetInt32();
    }
}

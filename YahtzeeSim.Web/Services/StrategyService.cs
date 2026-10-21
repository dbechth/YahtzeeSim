using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using YahtzeeSim.Analysis;
using YahtzeeSim.Analysis.Data;
using YahtzeeSim.Analysis.Reports;
using YahtzeeSim.Analysis.Solver;

namespace YahtzeeSim.Web.Services;

public enum StrategyStatus
{
    Ready,
    NoGames,
    Offline,
    SessionExpired,
    Failed
}

/// <summary>The outcome of building the player's report: the report markup when ready, plus how many games it covers.</summary>
public sealed record StrategyReport(StrategyStatus Status, string? Html = null, int Games = 0, int Skipped = 0, int Limit = 0);

/// <summary>
/// Builds the signed-in player's Full Sheet strategy report in the browser. Their recent finished games come from
/// <c>api/strategy</c>; the solver table and reference simulations are precomputed files shipped with the app, so the
/// report is the same rule-based analysis as scripts/analyze-strategy.ps1.
/// </summary>
public sealed class StrategyService(HttpClient http, AccountService account)
{
    private static readonly JsonSerializerOptions WebJson = new(JsonSerializerDefaults.Web);

    private Task<(CardSolver Solver, ReferenceData Reference)>? _assets;

    private sealed record Payload(string? Nickname, int Limit, List<PayloadGame> Games);

    private sealed record PayloadGame(string GameId, int Total, long PlayedAt, List<JsonElement[]> Actions);

    /// <summary>Downloads the games and runs the analysis. <paramref name="progress"/> receives short status lines.</summary>
    public async Task<StrategyReport> BuildAsync(Func<string, Task> progress)
    {
        if (account.UserId is not { } userId)
        {
            return new StrategyReport(StrategyStatus.SessionExpired);
        }

        await progress("Loading your games");
        Payload? payload;
        try
        {
            using var response = await http.GetAsync("api/strategy");
            if (response.StatusCode == HttpStatusCode.Unauthorized)
            {
                await account.HandleSessionExpiredAsync();
                return new StrategyReport(StrategyStatus.SessionExpired);
            }

            if (!response.IsSuccessStatusCode || response.Content.Headers.ContentType?.MediaType != "application/json")
            {
                return new StrategyReport(StrategyStatus.Offline);
            }

            payload = await response.Content.ReadFromJsonAsync<Payload>(WebJson);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            return new StrategyReport(StrategyStatus.Offline);
        }

        if (payload == null)
        {
            return new StrategyReport(StrategyStatus.Failed);
        }

        if (payload.Games.Count == 0)
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

        await progress($"Analysing {payload.Games.Count} game{(payload.Games.Count == 1 ? "" : "s")}");
        try
        {
            var data = ToExport(payload, userId, account.Nickname ?? payload.Nickname ?? "You");
            var result = AnalysisRunner.Run(data, new AnalysisOptions(), solver: assets.Solver, referenceData: assets.Reference);
            var player = result.Players.FirstOrDefault();
            return player == null
                ? new StrategyReport(StrategyStatus.NoGames, Skipped: result.Skipped.Count, Limit: payload.Limit)
                : new StrategyReport(StrategyStatus.Ready, HtmlReport.PlayerFragment(result, player), player.Games.Count, result.Skipped.Count, payload.Limit);
        }
        catch (Exception ex) when (ex is FormatException or InvalidOperationException or ArgumentException or KeyNotFoundException or IndexOutOfRangeException)
        {
            return new StrategyReport(StrategyStatus.Failed);
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

    private static ExportData ToExport(Payload payload, string userId, string nickname)
    {
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

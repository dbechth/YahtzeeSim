using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.JSInterop;
using YahtzeeSim.Engine;

namespace YahtzeeSim.Web.Services;

public enum GameActionType
{
    Roll,
    Hold,
    Unhold,
    Score,
    Scratch,
    Undo
}

/// <summary>Dice and turn position at one moment, captured from the engine before or after an action.</summary>
public readonly record struct TurnState(int Turn, int RollNumber, string Dice)
{
    public static TurnState Capture(YahtzeeGameEngine engine) => new(
        engine.TurnNumber ?? 0,
        engine.RollCount,
        string.Concat(engine.Dice.Select(d => d.Value)));
}

/// <summary>One thing the player did. Hold state is not stored; it is rebuilt from the Hold and Unhold presses.</summary>
public sealed record GameAction(
    int Seq,
    string Type,
    long At,
    int Turn,
    int Roll,
    string Dice,
    int? DieId,
    int? GameIndex,
    string? Category,
    int? Points,
    bool Bonus);

public sealed record PendingAction(string UserId, Guid GameId, GameMode Mode, GameAction Action);

/// <summary>
/// Records every roll, hold press, score and undo for gameplay research. Actions are queued in localStorage and uploaded
/// in batches (after each score or undo, and on launch or reconnect), so play never waits on the network.
/// The server ignores a (game, sequence number) it already has, which makes retries safe.
/// </summary>
public sealed class ActionLog(HttpClient http, IJSRuntime js, AccountService account)
{
    private const string QueueKey = "yahtzeesim.actionQueue";
    private const string SequenceKey = "yahtzeesim.actionSeq";
    private const int BatchSize = 100;

    private static readonly JsonSerializerOptions WebJson = new(JsonSerializerDefaults.Web);

    private readonly SemaphoreSlim _gate = new(1, 1);

    private sealed record Sequence(Guid GameId, int Next);

    public async Task RecordAsync(
        YahtzeeGameEngine engine,
        GameActionType type,
        TurnState state,
        int? dieId = null,
        int? gameIndex = null,
        ScoreCategory? category = null,
        int? points = null,
        bool bonus = false)
    {
        if (account.UserId is not { } userId)
        {
            return;
        }

        await _gate.WaitAsync();
        try
        {
            var sequence = await LoadSequenceAsync();
            int seq = sequence?.GameId == engine.GameId ? sequence.Next : 1;
            var action = new GameAction(
                seq,
                type.ToString(),
                DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                state.Turn,
                state.RollNumber,
                state.Dice,
                dieId,
                gameIndex,
                category?.ToString(),
                points,
                bonus);

            var items = await LoadQueueAsync();
            items.Add(new PendingAction(userId, engine.GameId, engine.Mode, action));
            await SaveAsync(QueueKey, items);
            await SaveAsync(SequenceKey, new Sequence(engine.GameId, seq + 1));
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>Uploads everything queued for the signed-in user. Stops at the first temporary failure and keeps the rest for later.</summary>
    public async Task FlushAsync()
    {
        if (account.State != AccountState.SignedIn || account.UserId is not { } userId)
        {
            return;
        }

        await _gate.WaitAsync();
        try
        {
            var items = await LoadQueueAsync();
            foreach (var game in items.Where(i => i.UserId == userId).GroupBy(i => i.GameId).ToList())
            {
                foreach (var chunk in game.OrderBy(i => i.Action.Seq).Chunk(BatchSize))
                {
                    var outcome = await TryUploadAsync(game.Key, chunk[0].Mode, chunk.Select(c => c.Action).ToList());
                    if (outcome == UploadOutcome.Retry)
                    {
                        return;
                    }

                    if (outcome == UploadOutcome.SessionExpired)
                    {
                        await account.HandleSessionExpiredAsync();
                        return;
                    }

                    foreach (var sent in chunk)
                    {
                        items.Remove(sent);
                    }

                    await SaveAsync(QueueKey, items);
                }
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    private enum UploadOutcome
    {
        Done,
        Retry,
        SessionExpired
    }

    private async Task<UploadOutcome> TryUploadAsync(Guid gameId, GameMode mode, List<GameAction> actions)
    {
        try
        {
            using var response = await http.PostAsJsonAsync("api/actions", new { gameId, mode = mode.ToString(), actions }, WebJson);
            if (response.StatusCode == HttpStatusCode.Unauthorized)
            {
                return UploadOutcome.SessionExpired;
            }

            // Rate limited, API not reachable here or a server hiccup: keep the actions and try again later.
            if (response.StatusCode is HttpStatusCode.TooManyRequests or HttpStatusCode.NotFound or HttpStatusCode.MethodNotAllowed
                || (int)response.StatusCode >= 500)
            {
                return UploadOutcome.Retry;
            }

            // Success, or a permanent rejection that retrying cannot fix.
            return UploadOutcome.Done;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            return UploadOutcome.Retry;
        }
    }

    private async Task<List<PendingAction>> LoadQueueAsync() => await LoadAsync<List<PendingAction>>(QueueKey) ?? [];

    private Task<Sequence?> LoadSequenceAsync() => LoadAsync<Sequence>(SequenceKey);

    private async Task<T?> LoadAsync<T>(string key)
    {
        try
        {
            string? json = await js.InvokeAsync<string?>("localStorage.getItem", key);
            return string.IsNullOrWhiteSpace(json) ? default : JsonSerializer.Deserialize<T>(json);
        }
        catch (Exception ex) when (ex is JSException or JsonException)
        {
            return default;
        }
    }

    private async Task SaveAsync<T>(string key, T value)
    {
        try
        {
            await js.InvokeVoidAsync("localStorage.setItem", key, JsonSerializer.Serialize(value));
        }
        catch (JSException)
        {
        }
    }
}

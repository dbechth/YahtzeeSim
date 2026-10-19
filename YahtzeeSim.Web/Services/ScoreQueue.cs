using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.JSInterop;
using YahtzeeSim.Engine;

namespace YahtzeeSim.Web.Services;

public sealed record PendingScore(string UserId, Guid GameId, GameMode Mode, int Total);

/// <summary>
/// Finished games waiting to be uploaded. They live in localStorage so a game completed offline (or just
/// before the tab closes) still reaches the leaderboard later. The server treats a game id as idempotent.
/// </summary>
public sealed class ScoreQueue(HttpClient http, IJSRuntime js, AccountService account)
{
    private const string QueueKey = "yahtzeesim.scoreQueue";

    private readonly SemaphoreSlim _gate = new(1, 1);

    public async Task<int> CountForCurrentUserAsync()
    {
        var items = await LoadAsync();
        return items.Count(i => i.UserId == account.UserId);
    }

    /// <summary>Queues a finished game and tries to upload straight away. Returns true if nothing is left waiting for this user.</summary>
    public async Task<bool> SubmitAsync(Guid gameId, GameMode mode, int total)
    {
        if (account.UserId is not { } userId)
        {
            return false;
        }

        await _gate.WaitAsync();
        try
        {
            var items = await LoadAsync();
            items.RemoveAll(i => i.UserId == userId && i.GameId == gameId);
            items.Add(new PendingScore(userId, gameId, mode, total));
            await SaveAsync(items);
        }
        finally
        {
            _gate.Release();
        }

        return await FlushAsync();
    }

    /// <summary>Drops a not-yet-uploaded score, e.g. when the final move of a finished game is undone.</summary>
    public async Task DiscardAsync(Guid gameId)
    {
        await _gate.WaitAsync();
        try
        {
            var items = await LoadAsync();
            if (items.RemoveAll(i => i.UserId == account.UserId && i.GameId == gameId) > 0)
            {
                await SaveAsync(items);
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>Uploads everything queued for the signed-in user. Returns true when nothing is left waiting for this user.</summary>
    public async Task<bool> FlushAsync()
    {
        if (account.State != AccountState.SignedIn || account.UserId is not { } userId)
        {
            return false;
        }

        await _gate.WaitAsync();
        try
        {
            var items = await LoadAsync();
            foreach (var item in items.Where(i => i.UserId == userId).ToList())
            {
                var outcome = await TryUploadAsync(item);
                if (outcome == UploadOutcome.Retry)
                {
                    break;
                }

                if (outcome == UploadOutcome.SessionExpired)
                {
                    await account.HandleSessionExpiredAsync();
                    break;
                }

                items.Remove(item);
                await SaveAsync(items);
            }

            return items.All(i => i.UserId != userId);
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

    private async Task<UploadOutcome> TryUploadAsync(PendingScore item)
    {
        try
        {
            using var response = await http.PostAsJsonAsync("api/scores", new { gameId = item.GameId, mode = item.Mode.ToString(), total = item.Total });
            if (response.StatusCode == HttpStatusCode.Unauthorized)
            {
                return UploadOutcome.SessionExpired;
            }

            // Rate limited, nickname missing, API not reachable here or a server hiccup: keep it and try again later.
            if (response.StatusCode is HttpStatusCode.TooManyRequests or HttpStatusCode.Conflict or HttpStatusCode.NotFound or HttpStatusCode.MethodNotAllowed
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

    private async Task<List<PendingScore>> LoadAsync()
    {
        try
        {
            string? json = await js.InvokeAsync<string?>("localStorage.getItem", QueueKey);
            return string.IsNullOrWhiteSpace(json) ? [] : JsonSerializer.Deserialize<List<PendingScore>>(json) ?? [];
        }
        catch (Exception ex) when (ex is JSException or JsonException)
        {
            return [];
        }
    }

    private async Task SaveAsync(List<PendingScore> items)
    {
        try
        {
            await js.InvokeVoidAsync("localStorage.setItem", QueueKey, JsonSerializer.Serialize(items));
        }
        catch (JSException)
        {
        }
    }
}

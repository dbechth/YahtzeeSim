using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;

namespace YahtzeeSim.Web.Services;

public sealed record ChallengeSummary(string Id, string Title, string Mode, string Creator, long Deadline, string Status, int Players, int Entries, int? MyTotal, bool Unread)
{
    public bool IsOpen => Status == "open";
}

public sealed record ChallengePlayer(string Nickname, bool IsMe, int? Total, long? FinishedAt, int? Rank);

public sealed record ChallengeDetail(
    string Id,
    string Title,
    string Mode,
    string Creator,
    long CreatedAt,
    long Deadline,
    string Status,
    string? InviteToken,
    List<ChallengePlayer> Players)
{
    public bool IsOpen => Status == "open";

    public ChallengePlayer? Me => Players.FirstOrDefault(p => p.IsMe);
}

public sealed record CreatedChallenge(string Id, string InviteToken, List<string> Missing);

/// <summary>Result of a challenge API call: the value, or a short error code (offline, not_found, rate_limited ...).</summary>
public sealed record ApiResult<T>(T? Value, string? Error)
{
    public bool Ok => Error == null;
}

/// <summary>
/// Challenges between players: list, create, join with an invite link, leave, and the unread count for the top bar.
/// Only the challenge's players can read it (the server enforces this).
/// </summary>
public sealed class ChallengeService(HttpClient http, IJSRuntime js, AccountService account, NavigationManager navigation)
{
    private const string PendingJoinKey = "yahtzeesim.pendingJoin";
    private static readonly JsonSerializerOptions WebJson = new(JsonSerializerDefaults.Web);

    private sealed record ListResponse(List<ChallengeSummary> Challenges);

    private sealed record PlayersResponse(List<string> Players);

    private sealed record JoinResponse(string Id);

    private sealed record InviteResponse(List<string> Added, List<string> Missing);

    private sealed record ErrorResponse(string? Error);

    public int UnreadCount { get; private set; }

    public event Action? Changed;

    public string InviteUrl(string token) => navigation.BaseUri + "?join=" + Uri.EscapeDataString(token);

    public async Task<ApiResult<List<ChallengeSummary>>> ListAsync()
    {
        var result = await SendAsync<ListResponse>(HttpMethod.Get, "api/challenges");
        if (result.Value is { } list)
        {
            int unread = list.Challenges.Count(c => c.Unread);
            if (unread != UnreadCount)
            {
                UnreadCount = unread;
                Changed?.Invoke();
            }

            return new(list.Challenges, null);
        }

        return new(null, result.Error);
    }

    /// <summary>Refreshes the unread count shown on the top bar; failures (offline) keep the last count.</summary>
    public async Task RefreshUnreadAsync()
    {
        if (account.State == AccountState.SignedIn)
        {
            await ListAsync();
        }
    }

    public Task<ApiResult<ChallengeDetail>> GetAsync(string id) =>
        SendAsync<ChallengeDetail>(HttpMethod.Get, "api/challenges/" + Uri.EscapeDataString(id));

    public Task<ApiResult<CreatedChallenge>> CreateAsync(string title, string mode, DateTimeOffset deadline, IEnumerable<string> nicknames) =>
        SendAsync<CreatedChallenge>(HttpMethod.Post, "api/challenges", new { title, mode, deadline = deadline.ToUnixTimeMilliseconds(), nicknames });

    public async Task<ApiResult<string>> JoinAsync(string token)
    {
        var result = await SendAsync<JoinResponse>(HttpMethod.Post, "api/challenges/join", new { token });
        return new(result.Value?.Id, result.Error);
    }

    public async Task<ApiResult<(List<string> Added, List<string> Missing)>> InviteAsync(string id, IEnumerable<string> nicknames)
    {
        var result = await SendAsync<InviteResponse>(HttpMethod.Post, $"api/challenges/{Uri.EscapeDataString(id)}/invite", new { nicknames });
        return result.Value is { } v ? new((v.Added, v.Missing), null) : new(default, result.Error);
    }

    public async Task<bool> LeaveAsync(string id) =>
        (await SendAsync<JsonElement>(HttpMethod.Post, $"api/challenges/{Uri.EscapeDataString(id)}/leave", new { })).Ok;

    public async Task MarkSeenAsync(string id)
    {
        await SendAsync<JsonElement>(HttpMethod.Post, $"api/challenges/{Uri.EscapeDataString(id)}/seen", new { });
        await RefreshUnreadAsync();
    }

    public async Task<List<string>> SearchPlayersAsync(string query)
    {
        var result = await SendAsync<PlayersResponse>(HttpMethod.Get, "api/players?q=" + Uri.EscapeDataString(query));
        return result.Value?.Players ?? [];
    }

    /// <summary>Keeps an invite link's token on the device so it survives signing in (which leaves the page).</summary>
    public async Task SavePendingJoinAsync(string token)
    {
        try
        {
            await js.InvokeVoidAsync("localStorage.setItem", PendingJoinKey, token);
        }
        catch (JSException)
        {
        }
    }

    public async Task<string?> TakePendingJoinAsync()
    {
        try
        {
            string? token = await js.InvokeAsync<string?>("localStorage.getItem", PendingJoinKey);
            if (!string.IsNullOrEmpty(token))
            {
                await js.InvokeVoidAsync("localStorage.removeItem", PendingJoinKey);
            }

            return string.IsNullOrEmpty(token) ? null : token;
        }
        catch (JSException)
        {
            return null;
        }
    }

    private async Task<ApiResult<T>> SendAsync<T>(HttpMethod method, string url, object? body = null)
    {
        try
        {
            using var request = new HttpRequestMessage(method, url);
            if (body != null)
            {
                request.Content = JsonContent.Create(body, options: WebJson);
            }

            using var response = await http.SendAsync(request);
            if (response.StatusCode == HttpStatusCode.Unauthorized)
            {
                await account.HandleSessionExpiredAsync();
                return new(default, "signed_out");
            }

            if (response.Content.Headers.ContentType?.MediaType != "application/json")
            {
                return new(default, "offline");
            }

            if (!response.IsSuccessStatusCode)
            {
                var error = await response.Content.ReadFromJsonAsync<ErrorResponse>(WebJson);
                return new(default, error?.Error ?? "failed");
            }

            return new(await response.Content.ReadFromJsonAsync<T>(WebJson), null);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            return new(default, "offline");
        }
    }
}

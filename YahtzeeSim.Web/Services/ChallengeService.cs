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

/// <summary>A group the player is in (Status "active") or asked to join (Status "pending"). Requests only counts for admins.</summary>
public sealed record GroupSummary(string Id, string Name, string Status, string Role, int Members, int Requests)
{
    public bool IsPending => Status == "pending";

    public bool IsAdmin => !IsPending && Role == "admin";
}

/// <summary>Membership is "none", "pending", "member" or "admin".</summary>
public sealed record GroupSearchResult(string Id, string Name, int Members, string Membership);

public sealed record GroupMember(string Nickname, string Role, bool IsMe)
{
    public bool IsAdmin => Role == "admin";
}

public sealed record GroupDetail(string Id, string Name, string MyRole, List<GroupMember> Members, List<string> Requests)
{
    public bool IsAdmin => MyRole == "admin";
}

/// <summary>Result of a challenge API call: the value, or a short error code (offline, not_found, rate_limited ...).</summary>
public sealed record ApiResult<T>(T? Value, string? Error)
{
    public bool Ok => Error == null;
}

/// <summary>
/// Challenges between players: list, create, join with an invite link, leave, and the unread count for the top bar.
/// Also the player's groups: find and join them (an admin approves), manage members and admins, and invite a group to a challenge.
/// Only the challenge's players can read it (the server enforces this).
/// </summary>
public sealed class ChallengeService(HttpClient http, IJSRuntime js, AccountService account, NavigationManager navigation)
{
    private const string PendingJoinKey = "yahtzeesim.pendingJoin";
    private static readonly JsonSerializerOptions WebJson = new(JsonSerializerDefaults.Web);

    private sealed record ListResponse(List<ChallengeSummary> Challenges, int PendingRequests);

    private sealed record GroupsResponse(List<GroupSummary> Groups);

    private sealed record GroupSearchResponse(List<GroupSearchResult> Groups);

    private sealed record IdResponse(string Id);
    private sealed record AddedResponse(bool Added);

    private sealed record PlayersResponse(List<string> Players);

    private sealed record JoinResponse(string Id);

    private sealed record InviteResponse(List<string> Added, List<string> Missing);

    private sealed record ErrorResponse(string? Error);

    /// <summary>Challenges with news plus group join requests waiting for this player to approve.</summary>
    public int UnreadCount { get; private set; }

    /// <summary>Group join requests waiting for this player to approve.</summary>
    public int PendingRequests { get; private set; }

    public event Action? Changed;

    public string InviteUrl(string token) => navigation.BaseUri + "?join=" + Uri.EscapeDataString(token);

    public async Task<ApiResult<List<ChallengeSummary>>> ListAsync()
    {
        var result = await SendAsync<ListResponse>(HttpMethod.Get, "api/challenges");
        if (result.Value is { } list)
        {
            int unread = list.Challenges.Count(c => c.Unread) + list.PendingRequests;
            if (unread != UnreadCount || list.PendingRequests != PendingRequests)
            {
                UnreadCount = unread;
                PendingRequests = list.PendingRequests;
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

    public Task<ApiResult<CreatedChallenge>> CreateAsync(string title, string mode, DateTimeOffset deadline, IEnumerable<string> nicknames, IEnumerable<string> groupIds) =>
        SendAsync<CreatedChallenge>(HttpMethod.Post, "api/challenges", new { title, mode, deadline = deadline.ToUnixTimeMilliseconds(), nicknames, groupIds });

    public async Task<ApiResult<string>> JoinAsync(string token)
    {
        var result = await SendAsync<JoinResponse>(HttpMethod.Post, "api/challenges/join", new { token });
        return new(result.Value?.Id, result.Error);
    }

    public async Task<ApiResult<(List<string> Added, List<string> Missing)>> InviteAsync(string id, IEnumerable<string> nicknames, IEnumerable<string> groupIds)
    {
        var result = await SendAsync<InviteResponse>(HttpMethod.Post, $"api/challenges/{Uri.EscapeDataString(id)}/invite", new { nicknames, groupIds });
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

    public async Task<ApiResult<List<GroupSummary>>> ListGroupsAsync()
    {
        var result = await SendAsync<GroupsResponse>(HttpMethod.Get, "api/groups");
        return new(result.Value?.Groups, result.Error);
    }

    public Task<ApiResult<GroupDetail>> GetGroupAsync(string id) =>
        SendAsync<GroupDetail>(HttpMethod.Get, GroupUrl(id));

    public async Task<ApiResult<string>> CreateGroupAsync(string name)
    {
        var result = await SendAsync<IdResponse>(HttpMethod.Post, "api/groups", new { name });
        return new(result.Value?.Id, result.Error);
    }

    public async Task<List<GroupSearchResult>> SearchGroupsAsync(string query)
    {
        var result = await SendAsync<GroupSearchResponse>(HttpMethod.Get, "api/groups/search?q=" + Uri.EscapeDataString(query));
        return result.Value?.Groups ?? [];
    }

    public async Task<string?> RequestToJoinGroupAsync(string id) =>
        (await SendAsync<JsonElement>(HttpMethod.Post, GroupUrl(id) + "/join", new { })).Error;

    /// <summary>An admin approves or declines a join request. Returns an error code, or null on success.</summary>
    public async Task<string?> DecideGroupRequestAsync(string id, string nickname, bool approve)
    {
        string? error = (await SendAsync<JsonElement>(HttpMethod.Post, GroupUrl(id) + "/requests", new { nickname, approve })).Error;
        await RefreshUnreadAsync();
        return error;
    }

    /// <summary>An admin adds a player to the group with no approval step. Value is false when they were already in.</summary>
    public async Task<ApiResult<bool>> AddGroupMemberAsync(string id, string nickname)
    {
        var result = await SendAsync<AddedResponse>(HttpMethod.Post, GroupUrl(id) + "/members", new { nickname });
        return new(result.Value?.Added ?? false, result.Error);
    }

    public async Task<string?> SetGroupRoleAsync(string id, string nickname, string role) =>
        (await SendAsync<JsonElement>(HttpMethod.Post, GroupUrl(id) + "/role", new { nickname, role })).Error;

    public async Task<string?> RemoveGroupMemberAsync(string id, string nickname) =>
        (await SendAsync<JsonElement>(HttpMethod.Post, GroupUrl(id) + "/remove", new { nickname })).Error;

    public async Task<string?> LeaveGroupAsync(string id)
    {
        string? error = (await SendAsync<JsonElement>(HttpMethod.Post, GroupUrl(id) + "/leave", new { })).Error;
        await RefreshUnreadAsync();
        return error;
    }

    private static string GroupUrl(string id) => "api/groups/" + Uri.EscapeDataString(id);

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

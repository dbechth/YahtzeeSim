using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.JSInterop;

namespace YahtzeeSim.Web.Services;

public enum AccountState
{
    Loading,
    SignedOut,
    SignedIn
}

public enum NicknameResult
{
    Saved,
    Invalid,
    Taken,
    Failed
}

/// <summary>
/// Tracks who is signed in. Authentication itself is done by the server through Google; the browser only
/// holds an HttpOnly session cookie plus a small cached profile so the app still opens offline.
/// </summary>
public sealed class AccountService(HttpClient http, IJSRuntime js)
{
    private const string ProfileKey = "yahtzeesim.account";

    private sealed record Profile(string Id, string? Nickname);

    private sealed record MeResponse(string? Id, string? Nickname);

    public AccountState State { get; private set; } = AccountState.Loading;
    public string? UserId { get; private set; }
    public string? Nickname { get; private set; }

    /// <summary>True when the profile came from the device cache because the server could not be reached.</summary>
    public bool IsOffline { get; private set; }

    /// <summary>Set while signed out when the server could not be reached and there is no cached profile.</summary>
    public bool ServerUnreachable { get; private set; }

    public event Action? Changed;

    private static readonly System.Text.RegularExpressions.Regex NicknameChars = new("^[A-Za-z0-9][A-Za-z0-9 _-]*[A-Za-z0-9]$");

    public static bool IsValidNickname(string nickname) =>
        nickname.Length is >= 3 and <= 16 && NicknameChars.IsMatch(nickname);

    public async Task InitializeAsync()
    {
        try
        {
            using var response = await http.GetAsync("api/me");
            if (response.IsSuccessStatusCode && IsJson(response))
            {
                var me = await response.Content.ReadFromJsonAsync<MeResponse>();
                if (!string.IsNullOrEmpty(me?.Id))
                {
                    await SetSignedInAsync(me.Id, me.Nickname, offline: false);
                    return;
                }
            }

            if (response.StatusCode == HttpStatusCode.Unauthorized && IsJson(response))
            {
                await SetSignedOutAsync(unreachable: false);
                return;
            }
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException or TaskCanceledException)
        {
            // Offline or the API is not deployed here: fall through to the cached profile.
        }

        var cached = await LoadProfileAsync();
        if (cached != null)
        {
            await SetSignedInAsync(cached.Id, cached.Nickname, offline: true);
        }
        else
        {
            await SetSignedOutAsync(unreachable: true);
        }
    }

    public async Task<NicknameResult> SetNicknameAsync(string nickname)
    {
        try
        {
            using var response = await http.PutAsJsonAsync("api/me", new { nickname });
            switch (response.StatusCode)
            {
                case HttpStatusCode.OK:
                    var me = await response.Content.ReadFromJsonAsync<MeResponse>();
                    Nickname = me?.Nickname ?? nickname;
                    await SaveProfileAsync();
                    Changed?.Invoke();
                    return NicknameResult.Saved;
                case HttpStatusCode.Conflict:
                    return NicknameResult.Taken;
                case HttpStatusCode.BadRequest:
                    return NicknameResult.Invalid;
                case HttpStatusCode.Unauthorized:
                    await SetSignedOutAsync(unreachable: false);
                    return NicknameResult.Failed;
                default:
                    return NicknameResult.Failed;
            }
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException or TaskCanceledException)
        {
            return NicknameResult.Failed;
        }
    }

    /// <summary>Called when any API call reports that the session is gone.</summary>
    public Task HandleSessionExpiredAsync() => SetSignedOutAsync(unreachable: false);

    public async Task SignOutAsync()
    {
        try
        {
            using var response = await http.PostAsJsonAsync("api/auth/logout", new { });
        }
        catch (HttpRequestException)
        {
            // Offline: this device is still signed out; the server session simply expires on its own.
        }

        await SetSignedOutAsync(unreachable: false);
    }

    private static bool IsJson(HttpResponseMessage response) =>
        response.Content.Headers.ContentType?.MediaType == "application/json";

    private async Task SetSignedInAsync(string id, string? nickname, bool offline)
    {
        State = AccountState.SignedIn;
        UserId = id;
        Nickname = nickname;
        IsOffline = offline;
        ServerUnreachable = false;
        await SaveProfileAsync();
        Changed?.Invoke();
    }

    private async Task SetSignedOutAsync(bool unreachable)
    {
        State = AccountState.SignedOut;
        UserId = null;
        Nickname = null;
        IsOffline = false;
        ServerUnreachable = unreachable;
        try
        {
            await js.InvokeVoidAsync("localStorage.removeItem", ProfileKey);
        }
        catch (JSException)
        {
        }

        Changed?.Invoke();
    }

    private async Task<Profile?> LoadProfileAsync()
    {
        try
        {
            string? json = await js.InvokeAsync<string?>("localStorage.getItem", ProfileKey);
            var profile = string.IsNullOrWhiteSpace(json) ? null : JsonSerializer.Deserialize<Profile>(json);
            return string.IsNullOrEmpty(profile?.Id) ? null : profile;
        }
        catch (Exception ex) when (ex is JSException or JsonException)
        {
            return null;
        }
    }

    private async Task SaveProfileAsync()
    {
        try
        {
            await js.InvokeVoidAsync("localStorage.setItem", ProfileKey, JsonSerializer.Serialize(new Profile(UserId!, Nickname)));
        }
        catch (JSException)
        {
        }
    }
}

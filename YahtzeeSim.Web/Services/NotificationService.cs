using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.JSInterop;

namespace YahtzeeSim.Web.Services;

public enum PushState
{
    /// <summary>Not available: the server has no keys yet, or this browser cannot do Web Push.</summary>
    Unavailable,

    /// <summary>iPhone/iPad in the browser: notifications need the app added to the Home Screen first.</summary>
    NeedsInstall,

    /// <summary>The player blocked notifications for the app in browser or phone settings.</summary>
    Blocked,

    Off,
    On
}

/// <summary>Challenge notifications on this device (Web Push): status, turning them on or off.</summary>
public sealed class NotificationService(HttpClient http, IJSRuntime js)
{
    private static readonly JsonSerializerOptions WebJson = new(JsonSerializerDefaults.Web);

    private string? _publicKey;
    private bool _keyLoaded;

    private sealed record JsStatus(string State, bool Subscribed);

    private sealed record KeyResponse(string PublicKey);

    private sealed record JsSubscription(string? Endpoint, string? P256dh, string? Auth, string? Error);

    /// <summary>Also fetches the server key, so a later tap on "Turn on" can ask for permission straight away.</summary>
    public async Task<PushState> GetStateAsync()
    {
        if (!_keyLoaded)
        {
            try
            {
                using var response = await http.GetAsync("api/push/key");
                if (response.IsSuccessStatusCode && response.Content.Headers.ContentType?.MediaType == "application/json")
                {
                    _publicKey = (await response.Content.ReadFromJsonAsync<KeyResponse>(WebJson))?.PublicKey;
                    _keyLoaded = true;
                }
                else if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
                {
                    _keyLoaded = true;
                }
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
            {
            }
        }

        JsStatus status;
        try
        {
            status = await js.InvokeAsync<JsStatus>("yahtzeeSimPush.status");
        }
        catch (JSException)
        {
            return PushState.Unavailable;
        }

        return status.State switch
        {
            "needs-install" => PushState.NeedsInstall,
            "unsupported" => PushState.Unavailable,
            _ when string.IsNullOrEmpty(_publicKey) => PushState.Unavailable,
            "denied" => PushState.Blocked,
            _ => status.Subscribed ? PushState.On : PushState.Off
        };
    }

    /// <summary>Asks for permission and registers this device. Call directly from a tap.</summary>
    public async Task<PushState> TurnOnAsync()
    {
        if (string.IsNullOrEmpty(_publicKey))
        {
            return PushState.Unavailable;
        }

        JsSubscription subscription;
        try
        {
            subscription = await js.InvokeAsync<JsSubscription>("yahtzeeSimPush.subscribe", _publicKey);
        }
        catch (JSException)
        {
            return PushState.Unavailable;
        }

        if (subscription.Error != null || subscription.Endpoint == null)
        {
            return subscription.Error == "denied" ? PushState.Blocked : PushState.Off;
        }

        try
        {
            using var response = await http.PutAsJsonAsync("api/push/subscription", new { subscription.Endpoint, subscription.P256dh, subscription.Auth }, WebJson);
            return response.IsSuccessStatusCode ? PushState.On : PushState.Off;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            return PushState.Off;
        }
    }

    public async Task<PushState> TurnOffAsync()
    {
        try
        {
            string? endpoint = await js.InvokeAsync<string?>("yahtzeeSimPush.unsubscribe");
            if (endpoint != null)
            {
                using var request = new HttpRequestMessage(HttpMethod.Delete, "api/push/subscription")
                {
                    Content = JsonContent.Create(new { endpoint }, options: WebJson)
                };
                using var _ = await http.SendAsync(request);
            }
        }
        catch (Exception ex) when (ex is JSException or HttpRequestException or TaskCanceledException)
        {
        }

        return await GetStateAsync();
    }

    /// <summary>Shares a link with the phone's share sheet, or copies it. Returns shared, copied, cancelled or failed.</summary>
    public async Task<string> ShareAsync(string title, string text, string url)
    {
        try
        {
            return await js.InvokeAsync<string>("yahtzeeSimPush.share", title, text, url);
        }
        catch (JSException)
        {
            return "failed";
        }
    }
}

using System.Diagnostics;
using System.Net.Http;
using System.Text;

namespace TermLock.Core;

/// <summary>
/// Fire-and-forget ntfy notifications. Never blocks the lock screen.
/// The topic is a secret: it stays in config and is never logged.
/// </summary>
public sealed class NtfyNotifier
{
    private static readonly HttpClient Http = new()
    {
        Timeout = TimeSpan.FromSeconds(5)
    };

    private readonly AppConfig _config;
    private DateTime _lastSent = DateTime.MinValue;

    public NtfyNotifier(AppConfig config)
    {
        _config = config;
    }

    public void Notify(
        string title,
        string message,
        string tags = "warning",
        string priority = "default",
        bool force = false)
    {
        if (!_config.NtfyEnabled || string.IsNullOrWhiteSpace(_config.NtfyTopic))
        {
            return;
        }

        var now = DateTime.UtcNow;
        var interval = TimeSpan.FromSeconds(Math.Max(1, _config.NtfyMinIntervalSeconds));

        if (!force && now - _lastSent < interval)
        {
            return;
        }

        _lastSent = now;
        _ = SendAsync(title, message, tags, priority);
    }

    private async Task SendAsync(
        string title,
        string message,
        string tags,
        string priority)
    {
        try
        {
            var baseUrl = string.IsNullOrWhiteSpace(_config.NtfyUrl)
                ? "https://ntfy.sh"
                : _config.NtfyUrl.TrimEnd('/');

            var url = $"{baseUrl}/{Uri.EscapeDataString(_config.NtfyTopic.Trim())}";

            using var request = new HttpRequestMessage(HttpMethod.Post, url);
            request.Headers.TryAddWithoutValidation("Title", title);
            request.Headers.TryAddWithoutValidation("Priority", priority);
            request.Headers.TryAddWithoutValidation("Tags", tags);
            request.Content = new StringContent(message, Encoding.UTF8);

            using var response = await Http.SendAsync(request);

            if (!response.IsSuccessStatusCode)
            {
                Log.Write($"ntfy failed: HTTP {(int)response.StatusCode}");
            }
        }
        catch (Exception ex)
        {
            Log.Write("ntfy failed: " + ex.GetType().Name);
        }
    }
}

using System.Net.Http.Json;
using System.Text;

namespace Crooked.Services
{
    // Kinds of data a page can be showing. A broadcast names the ones that changed.
    public static class LiveTopics
    {
        public const string Transactions = "transactions";
        public const string Products = "products";
        public const string Exchanges = "exchanges";
        public const string Activity = "activity";
        public const string Staff = "staff";
        public const string Forecast = "forecast";
        public const string Notifications = "notifications";
    }

    // Tells open pages that data changed, over a Supabase Realtime broadcast channel (websockets).
    // Pages subscribe with the public anon key (see live.js) and reload what they show. Messages only
    // name the kinds of data that changed, never the data itself.
    // Off unless the SUPABASE_URL and SUPABASE_ANON_KEY environment variables are set.
    public sealed class LiveUpdates
    {
        public const string Channel = "crkd-live";
        public const string Event = "change";
        private const string CheckChannel = "crkd-live-check";

        private readonly IHttpClientFactory _httpClientFactory;
        private readonly ILogger<LiveUpdates> _logger;

        public LiveUpdates(IConfiguration configuration, IHttpClientFactory httpClientFactory, ILogger<LiveUpdates> logger)
        {
            _httpClientFactory = httpClientFactory;
            _logger = logger;

            var url = Clean(configuration["SUPABASE_URL"]);
            var anonKey = Clean(configuration["SUPABASE_ANON_KEY"]);
            if (url == null || anonKey == null)
            {
                Problem = "SUPABASE_URL and SUPABASE_ANON_KEY are not both set.";
                return;
            }

            // Keep only https://<project-ref>.supabase.co, even if a path such as /rest/v1 was pasted too.
            if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || (uri.Scheme != "https" && uri.Scheme != "http"))
            {
                Problem = $"SUPABASE_URL \"{url}\" is not a web address. Use the Project URL, like https://<project-ref>.supabase.co.";
                logger.LogError("Live updates are off: {Problem}", Problem);
                return;
            }

            KeyType = DescribeKey(anonKey);
            // A secret key would be handed to every browser, so it is never used.
            if (KeyType.StartsWith("secret", StringComparison.Ordinal))
            {
                Problem = "SUPABASE_ANON_KEY holds a secret key. Use the anon (public) key instead, and replace the secret key in Supabase.";
                logger.LogError("Live updates are off: {Problem}", Problem);
                return;
            }

            Url = uri.GetLeftPart(UriPartial.Authority);
            AnonKey = anonKey;
            if (!uri.Host.EndsWith(".supabase.co", StringComparison.OrdinalIgnoreCase))
            {
                logger.LogWarning("SUPABASE_URL host {Host} is not a supabase.co project URL; live updates may not connect.", uri.Host);
            }
            logger.LogInformation("Live updates are on for {Url} using the {KeyType}.", Url, KeyType);
        }

        public string? Url { get; }
        public string? AnonKey { get; }
        public string KeyType { get; } = "none";
        // Why live updates are off, when they are.
        public string? Problem { get; }
        public bool Enabled => Url != null;

        // Fire and forget: a failed broadcast must never fail the request that changed the data.
        public void Publish(params string[] topics)
        {
            if (!Enabled || topics.Length == 0) return;
            _ = PublishAsync(topics);
        }

        // Sends a test message on a channel no page listens to and returns Supabase's answer.
        public Task<(int Status, string Body)> CheckAsync() => SendAsync(CheckChannel, new[] { "check" });

        private async Task PublishAsync(string[] topics)
        {
            var (status, body) = await SendAsync(Channel, topics);
            if (status is < 200 or >= 300)
            {
                _logger.LogWarning("Live update broadcast failed with {Status}: {Body}", status, body);
            }
        }

        private async Task<(int Status, string Body)> SendAsync(string channel, string[] topics)
        {
            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Post, $"{Url}/realtime/v1/api/broadcast");
                request.Headers.Add("apikey", AnonKey);
                request.Content = JsonContent.Create(new
                {
                    messages = new[] { new { topic = channel, @event = Event, payload = new { topics } } }
                });

                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                using var response = await _httpClientFactory.CreateClient(nameof(LiveUpdates)).SendAsync(request, timeout.Token);
                return ((int)response.StatusCode, await response.Content.ReadAsStringAsync());
            }
            catch (Exception ex)
            {
                return (0, ex.Message);
            }
        }

        private static string? Clean(string? value)
        {
            var cleaned = value?.Trim().Trim('"', '\'').Trim();
            return string.IsNullOrEmpty(cleaned) ? null : cleaned;
        }

        // Names the kind of key without revealing it: legacy keys are JWTs whose payload has a role.
        private static string DescribeKey(string key)
        {
            if (key.StartsWith("sb_publishable_", StringComparison.Ordinal)) return "publishable key";
            if (key.StartsWith("sb_secret_", StringComparison.Ordinal)) return "secret key";

            var parts = key.Split('.');
            if (parts.Length == 3)
            {
                try
                {
                    var payload = parts[1].Replace('-', '+').Replace('_', '/');
                    payload = payload.PadRight(payload.Length + (4 - payload.Length % 4) % 4, '=');
                    var json = Encoding.UTF8.GetString(Convert.FromBase64String(payload));
                    if (json.Contains("\"service_role\"", StringComparison.Ordinal)) return "secret service_role key";
                    if (json.Contains("\"anon\"", StringComparison.Ordinal)) return "anon key";
                }
                catch (FormatException)
                {
                    // Not a JWT after all.
                }
            }
            return "unrecognised key";
        }
    }
}

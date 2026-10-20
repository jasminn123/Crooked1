using System.Net.Http.Json;

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

        private readonly IHttpClientFactory _httpClientFactory;
        private readonly ILogger<LiveUpdates> _logger;

        public LiveUpdates(IConfiguration configuration, IHttpClientFactory httpClientFactory, ILogger<LiveUpdates> logger)
        {
            _httpClientFactory = httpClientFactory;
            _logger = logger;
            var url = configuration["SUPABASE_URL"]?.Trim().TrimEnd('/');
            var anonKey = configuration["SUPABASE_ANON_KEY"]?.Trim();
            if (!string.IsNullOrEmpty(url) && !string.IsNullOrEmpty(anonKey))
            {
                Url = url;
                AnonKey = anonKey;
            }
        }

        public string? Url { get; }
        public string? AnonKey { get; }
        public bool Enabled => Url != null;

        // Fire and forget: a failed broadcast must never fail the request that changed the data.
        public void Publish(params string[] topics)
        {
            if (!Enabled || topics.Length == 0) return;
            _ = SendAsync(topics);
        }

        private async Task SendAsync(string[] topics)
        {
            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Post, $"{Url}/realtime/v1/api/broadcast");
                request.Headers.Add("apikey", AnonKey);
                request.Content = JsonContent.Create(new
                {
                    messages = new[] { new { topic = Channel, @event = Event, payload = new { topics } } }
                });

                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                using var response = await _httpClientFactory.CreateClient(nameof(LiveUpdates)).SendAsync(request, timeout.Token);
                if (!response.IsSuccessStatusCode)
                {
                    _logger.LogWarning("Live update broadcast failed with {Status}: {Body}",
                        (int)response.StatusCode, await response.Content.ReadAsStringAsync());
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning("Live update broadcast failed: {Message}", ex.Message);
            }
        }
    }
}

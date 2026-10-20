using Microsoft.AspNetCore.Mvc;
using Crooked.Services;

namespace Crooked.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class RealtimeController : ControllerBase
    {
        private readonly LiveUpdates _liveUpdates;

        public RealtimeController(LiveUpdates liveUpdates)
        {
            _liveUpdates = liveUpdates;
        }

        // Connection details for live.js. The anon key is meant to be public; row level security
        // (Database/schema.sql) keeps it from reading the shop's tables.
        [HttpGet("config")]
        public IActionResult GetConfig()
        {
            object config = _liveUpdates.Enabled
                ? new { enabled = true, url = _liveUpdates.Url, anonKey = _liveUpdates.AnonKey, channel = LiveUpdates.Channel, @event = LiveUpdates.Event }
                : new { enabled = false };
            return Ok(config);
        }

        // Troubleshooting: checks SUPABASE_URL and SUPABASE_ANON_KEY by sending a test broadcast
        // (on a channel no page listens to) and reports Supabase's answer. Open it in a browser.
        [HttpGet("check")]
        public async Task<IActionResult> Check()
        {
            if (!_liveUpdates.Enabled)
            {
                return Ok(new { ok = false, message = $"Live updates are off. {_liveUpdates.Problem}" });
            }

            var (status, body) = await _liveUpdates.CheckAsync();
            var ok = status is >= 200 and < 300;
            var message = ok
                ? "The server can reach Supabase Realtime with these settings. If the Live pill still doesn't connect, hover over it to see why."
                : status switch
                {
                    0 => $"The server could not reach {_liveUpdates.Url}. Check SUPABASE_URL ({body}).",
                    401 or 403 => "Supabase rejected the key. Check SUPABASE_ANON_KEY, and that Realtime is enabled for the project.",
                    404 => "Supabase has no Realtime endpoint at this address. Check SUPABASE_URL.",
                    _ => "Supabase returned an error; see supabaseResponse."
                };
            return Ok(new
            {
                ok,
                message,
                url = _liveUpdates.Url,
                keyType = _liveUpdates.KeyType,
                supabaseStatus = status,
                supabaseResponse = body
            });
        }
    }
}

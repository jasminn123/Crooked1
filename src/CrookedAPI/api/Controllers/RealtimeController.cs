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
    }
}

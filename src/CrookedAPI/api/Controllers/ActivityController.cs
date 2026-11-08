using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Npgsql;
using Crooked.Models;
using Crooked.Services;

namespace Crooked.Controllers
{
    // The activity log (who did what, and when) for the Activity Logs page. Entries are written with ActivityLog.
    [Route("api/[controller]")]
    [ApiController]
    [Authorize(Roles = UserRoles.Owner)]
    public class ActivityController : ControllerBase
    {
        private const int MaxPageSize = 50;
        private readonly string _connectionString = DatabaseConfig.ConnectionString;

        // One page of entries, newest first, with search and filters applied.
        [HttpGet]
        public async Task<IActionResult> GetActivity([FromQuery] ActivityQuery query)
        {
            var pageSize = Math.Clamp(query.PageSize, 1, MaxPageSize);
            await using var connection = new NpgsqlConnection(_connectionString);
            await connection.OpenAsync();

            int totalItems;
            await using (var countCommand = new NpgsqlCommand { Connection = connection })
            {
                countCommand.CommandText = $"SELECT COUNT(*) FROM activity_logs {BuildFilter(query, countCommand)}";
                totalItems = Convert.ToInt32(await countCommand.ExecuteScalarAsync());
            }

            var totalPages = Math.Max(1, (int)Math.Ceiling(totalItems / (double)pageSize));
            var page = Math.Clamp(query.Page, 1, totalPages);
            var items = new List<ActivityView>();
            await using (var command = new NpgsqlCommand { Connection = connection })
            {
                command.CommandText = $@"
                    SELECT date_occurred, COALESCE(staff_name, 'Unknown'), COALESCE(NULLIF(activity_type, ''), @other), COALESCE(action, '')
                    FROM activity_logs
                    {BuildFilter(query, command)}
                    ORDER BY date_occurred DESC
                    LIMIT @limit OFFSET @offset";
                command.Parameters.AddWithValue("@other", ActivityTypes.Other);
                command.Parameters.AddWithValue("@limit", pageSize);
                command.Parameters.AddWithValue("@offset", (page - 1) * pageSize);
                await using var reader = await command.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                {
                    // Stored in UTC; shown in Philippine time so the browser doesn't read it as its own local time.
                    var occurred = reader.GetDateTime(0);
                    items.Add(new ActivityView(
                        ShopClock.Display(occurred), ShopClock.Iso(occurred), reader.GetString(1), reader.GetString(2), reader.GetString(3)));
                }
            }

            return Ok(new { items, page, pageSize, totalItems, totalPages });
        }

        // Everyone who has an entry, for the Staff filter.
        [HttpGet("staff")]
        public async Task<IActionResult> GetStaffNames()
        {
            await using var connection = new NpgsqlConnection(_connectionString);
            await connection.OpenAsync();
            await using var command = new NpgsqlCommand(
                "SELECT DISTINCT staff_name FROM activity_logs WHERE COALESCE(staff_name, '') <> '' ORDER BY staff_name", connection);
            await using var reader = await command.ExecuteReaderAsync();
            var names = new List<string>();
            while (await reader.ReadAsync()) names.Add(reader.GetString(0));
            return Ok(names);
        }

        // WHERE clause for the search, type, staff and date filters; adds its parameters to command.
        private static string BuildFilter(ActivityQuery query, NpgsqlCommand command)
        {
            var conditions = new List<string>();

            var search = query.Search?.Trim();
            if (!string.IsNullOrEmpty(search))
            {
                var pattern = "%" + search.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_") + "%";
                command.Parameters.AddWithValue("@search", pattern);
                conditions.Add("(action ILIKE @search OR staff_name ILIKE @search)");
            }

            if (ActivityTypes.Normalize(query.Type) is string type)
            {
                command.Parameters.AddWithValue("@type", type);
                conditions.Add(type == ActivityTypes.Other
                    ? "activity_type IN (@type, '')"
                    : "activity_type = @type");
            }

            var staff = query.Staff?.Trim();
            if (!string.IsNullOrEmpty(staff))
            {
                command.Parameters.AddWithValue("@staff", staff);
                conditions.Add("staff_name = @staff");
            }

            if (query.From is DateOnly from)
            {
                command.Parameters.AddWithValue("@from", ShopClock.StartOfDayUtc(from));
                conditions.Add("date_occurred >= @from");
            }

            if (query.To is DateOnly to)
            {
                command.Parameters.AddWithValue("@to", ShopClock.StartOfDayUtc(to.AddDays(1)));
                conditions.Add("date_occurred < @to");
            }

            return conditions.Count == 0 ? "" : "WHERE " + string.Join(" AND ", conditions);
        }
    }
}

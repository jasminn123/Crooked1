using Npgsql;

namespace Crooked.Services
{
    // Writes activity_logs entries, shown on the Activity Logs page.
    // staffName is who did it (SessionClaims.FullNameOf(User) when signed in); type is an ActivityTypes
    // value. Pass the open transaction so the entry is saved only if the change is.
    public static class ActivityLog
    {
        private const string InsertSql = @"
            INSERT INTO activity_logs (staff_name, action, activity_type, date_occurred)
            VALUES (@name, @action, @type, NOW())";

        public static async Task WriteAsync(
            NpgsqlConnection connection, NpgsqlTransaction? transaction, string staffName, string type, string action)
        {
            await using var command = Command(connection, transaction, staffName, type, action);
            await command.ExecuteNonQueryAsync();
        }

        public static void Write(NpgsqlConnection connection, string staffName, string type, string action)
        {
            using var command = Command(connection, null, staffName, type, action);
            command.ExecuteNonQuery();
        }

        private static NpgsqlCommand Command(
            NpgsqlConnection connection, NpgsqlTransaction? transaction, string staffName, string type, string action)
        {
            var command = new NpgsqlCommand(InsertSql, connection, transaction);
            command.Parameters.AddWithValue("@name", string.IsNullOrWhiteSpace(staffName) ? "Unknown" : staffName.Trim());
            command.Parameters.AddWithValue("@action", action);
            command.Parameters.AddWithValue("@type", type);
            return command;
        }
    }
}

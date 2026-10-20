using Npgsql;

namespace Crooked
{
    // Applies Database/schema.sql (embedded in the app) at startup.
    public static class DatabaseSetup
    {
        public static void EnsureSchema()
        {
            using var connection = new NpgsqlConnection(DatabaseConfig.ConnectionString);
            connection.Open();

            using var command = new NpgsqlCommand(ReadSchema(), connection);
            command.ExecuteNonQuery();
        }

        private static string ReadSchema()
        {
            using var stream = typeof(DatabaseSetup).Assembly.GetManifestResourceStream("schema.sql")
                ?? throw new InvalidOperationException("Database/schema.sql is missing from the build.");
            using var reader = new StreamReader(stream);
            return reader.ReadToEnd();
        }
    }
}

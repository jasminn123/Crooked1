namespace Crooked
{
    public static class DatabaseConfig
    {
        public static readonly string ConnectionString =
            Environment.GetEnvironmentVariable("DB_CONNECTION_STRING") ??
            "Host=aws-0-ap-southeast-1.pooler.supabase.com;Database=postgres;Username=postgres.qpsfbvqkkfvthgsilefl;Password=ITgirljsmnn25;SSL Mode=Require;Trust Server Certificate=true";
    }
}
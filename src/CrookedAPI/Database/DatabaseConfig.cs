namespace CrookedAPI
{
    public static class DatabaseConfig
    {
        public static readonly string DatabaseName = "crooked1";
        public static readonly string RootConnectionString = "Server=localhost;Uid=root;Pwd=;";
        public static readonly string ConnectionString = $"Server=localhost;Database={DatabaseName};Uid=root;Pwd=;";
    }
}  // Pag nag error database after mo mag login, gawin mo palitan mo yung "user" ng "root"

using System.Collections.Concurrent;
using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Npgsql;

namespace Crooked.Services
{
    // Keeps signed-in sessions in step with their accounts. The session cookie carries the role the
    // user had at login, so without this check an owner who was changed to staff (or an archived
    // account) would keep their old access until they logged out. Program.cs runs ValidateAsync on
    // every API request; a session whose account is archived or has a different role is ended, the
    // API answers 401 and session.js sends that browser to the login page.
    // Each account's role is cached briefly so most requests don't query the database.
    public static class AccountSessions
    {
        private static readonly TimeSpan CacheFor = TimeSpan.FromSeconds(30);
        private static readonly ConcurrentDictionary<string, (string? Role, DateTime CheckedUtc)> Roles = new();

        // Call after archiving an account or changing its role, so the change applies on the next request.
        public static void Forget(string username) => Roles.TryRemove(username, out _);

        public static async Task ValidateAsync(CookieValidatePrincipalContext context)
        {
            var username = context.Principal?.Identity?.Name;
            if (string.IsNullOrEmpty(username)) return;

            string? currentRole;
            try
            {
                currentRole = await CurrentRoleAsync(username);
            }
            catch (Exception ex)
            {
                // If the database can't be reached the request fails anyway; don't sign everyone out.
                Console.WriteLine($"Session check failed: {ex.Message}");
                return;
            }

            if (currentRole != context.Principal!.FindFirstValue(ClaimTypes.Role))
            {
                context.RejectPrincipal();
                await context.HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            }
        }

        // The account's role in lower case (as the login stores it), or null when the account is archived or gone.
        private static async Task<string?> CurrentRoleAsync(string username)
        {
            if (Roles.TryGetValue(username, out var cached) && DateTime.UtcNow - cached.CheckedUtc < CacheFor)
            {
                return cached.Role;
            }

            await using var connection = new NpgsqlConnection(DatabaseConfig.ConnectionString);
            await connection.OpenAsync();
            await using var command = new NpgsqlCommand(
                "SELECT lower(role) FROM users WHERE username = @user AND is_active = TRUE LIMIT 1", connection);
            command.Parameters.AddWithValue("@user", username);
            var role = await command.ExecuteScalarAsync() as string;
            Roles[username] = (role, DateTime.UtcNow);
            return role;
        }
    }
}

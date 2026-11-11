using System.Security.Claims;

namespace Crooked.Models
{
    // Values of users.role. The signed-in session carries the same value as its role claim,
    // so [Authorize(Roles = UserRoles.Owner)] matches it.
    public static class UserRoles
    {
        public const string Owner = "owner";
        public const string Staff = "staff";

        public static string? Normalize(string? role) => role?.Trim().ToLowerInvariant() switch
        {
            Owner => Owner,
            Staff => Staff,
            _ => null
        };

        // "Owner" or "Staff", for messages and the activity log.
        public static string Display(string role) => role == Owner ? "Owner" : "Staff";
    }

    // Claims in the session cookie besides the standard name (username) and role (AuthController.Login).
    public static class SessionClaims
    {
        public const string FullName = "full_name";

        // The signed-in person's name for the activity log.
        public static string FullNameOf(ClaimsPrincipal user) =>
            user.FindFirstValue(FullName) is { Length: > 0 } name ? name : user.Identity?.Name ?? "Unknown";
    }
}

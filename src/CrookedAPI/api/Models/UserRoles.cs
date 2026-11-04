namespace Crooked.Models
{
    // Values of users.role. The signed-in session carries the same value as its role claim,
    // so [Authorize(Roles = UserRoles.Owner)] matches it.
    public static class UserRoles
    {
        public const string Owner = "owner";
        public const string Staff = "staff";
    }
}

namespace Crooked.Models
{
    public class User
    {
        public int Id { get; set; }
        public string Username { get; set; } = string.Empty;
        public string Password { get; set; } = string.Empty;
        public string Role { get; set; } = string.Empty;
        public string FullName { get; set; } = string.Empty;
    }

    public class LoginRequest
    {
        public string Username { get; set; } = string.Empty;
        public string Password { get; set; } = string.Empty;
    }

    // A signed-in user changing their own password (profile menu > Change Password).
    public class ChangePasswordRequest
    {
        public string Username { get; set; } = string.Empty;
        public string CurrentPassword { get; set; } = string.Empty;
        public string NewPassword { get; set; } = string.Empty;
    }

    // The owner setting a staff member's password (Staff Management).
    public class SetPasswordRequest
    {
        public string NewPassword { get; set; } = string.Empty;
    }

    // Rules for every new password: staff accounts, password changes and the owner's password reset.
    // Logging in does not check them, so older passwords keep working.
    // ui.js (isValidPassword) checks the same rules before a form is sent.
    public static class PasswordPolicy
    {
        public const int MinLength = 8;
        public const int MaxLength = 64;


        public static bool IsValid(string? password) =>
            password is { Length: >= MinLength and <= MaxLength }
            && password.Any(char.IsAsciiLetterUpper)
            && password.Any(char.IsAsciiLetterLower)
            && password.Any(char.IsAsciiDigit)
            && password.Any(c => !char.IsAsciiLetterOrDigit(c) && !char.IsWhiteSpace(c))
            && !password.Any(char.IsWhiteSpace);
    }
}
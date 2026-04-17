namespace CrookedAPI.Models
{
    public class ResetRequest 
    {
        public string Username { get; set; }
        public string RecoveryKey { get; set; }
        public string NewPassword { get; set; }
    }
}
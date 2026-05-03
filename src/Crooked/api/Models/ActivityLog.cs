namespace Crooked.Models
{
    public class ActivityLog
    {
        public int Id { get; set; }
        public string StaffName { get; set; }
        public string Action { get; set; }
        public DateTime DateOccurred { get; set; }
    }
}
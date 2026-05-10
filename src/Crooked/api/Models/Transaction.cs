using Crooked.Models;

namespace Crooked.Models
{
    public class Transaction
    {
        public int Transaction_Id { get; set; }
        public string ReferenceId { get; set; }
        public DateTime Date_Time { get; set; }
        public decimal Total_Amount { get; set; }
        public string Status { get; set; }
    }
}
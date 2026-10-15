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
        public string AssistedBy { get; set; } = "Unknown";
        public List<TransactionItemInput> Items { get; set; } = new();
    }

    public sealed class TransactionItemInput
    {
        public int ProductId { get; set; }
        public int Quantity { get; set; }
    }

    public sealed class ExchangeTransactionRequest
    {
        public List<ExchangeTransactionItem> Items { get; set; } = new();
    }

    public sealed class ExchangeTransactionItem
    {
        public long TransactionItemId { get; set; }
        public int ProductId { get; set; }
        public string Reason { get; set; } = string.Empty;
    }
}
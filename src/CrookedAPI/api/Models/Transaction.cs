using System.Text.Json.Serialization;

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

        [JsonPropertyName("payment_method")]
        public string? PaymentMethod { get; set; }

        public List<TransactionItemInput> Items { get; set; } = new();
    }

    public sealed class TransactionItemInput
    {
        public int ProductId { get; set; }
        public int Quantity { get; set; }
    }

    // Search, filters and paging for the transaction logs and the Excel export.
    public sealed class TransactionQuery
    {
        public string? Search { get; set; }
        public string? Status { get; set; }
        public string? Payment { get; set; }
        public DateOnly? From { get; set; }
        public DateOnly? To { get; set; }
        public int Page { get; set; } = 1;
        public int PageSize { get; set; } = 10;
    }
}

namespace Crooked.Models
{
    // Rows of the Transactions and Items sheets used by the Excel export and import.
    public sealed record TransactionSheetRow(
        int TransactionId,
        string ReferenceId,
        DateTime DateTimeUtc,
        string PaymentMethod,
        string SoldBy,
        string Status,
        decimal TotalAmount);

    public sealed record TransactionItemSheetRow(
        string ReferenceId,
        string ProductName,
        string Category,
        string Size,
        string Color,
        int Quantity,
        decimal UnitPrice,
        string ExchangeReason);

    public sealed class ImportedTransaction
    {
        public int Row { get; init; }
        public string ReferenceId { get; init; } = "";
        public DateTime DateTimeUtc { get; init; }
        public decimal TotalAmount { get; init; }
        public string PaymentMethod { get; init; } = "Unknown";
        public string SoldBy { get; init; } = "Unknown";
        public string Status { get; init; } = "Completed";
        public bool HasExchange { get; init; }
        public List<ImportedTransactionItem> Items { get; } = new();
    }

    public sealed class ImportedTransactionItem
    {
        public int Row { get; init; }
        public string ProductName { get; init; } = "";
        public string Category { get; init; } = "";
        public string Size { get; init; } = "";
        public string Color { get; init; } = "";
        public int Quantity { get; init; }
        public decimal UnitPrice { get; init; }
        public string ExchangeReason { get; init; } = "";
        public int ProductId { get; set; }
    }

    public sealed record ImportIssue(string Sheet, int Row, string Message);

    public sealed class ParsedWorkbook
    {
        public List<ImportedTransaction> Transactions { get; } = new();
        public List<ImportIssue> Issues { get; } = new();
        // Transactions left out because their row or one of their items had a problem.
        public int Rejected { get; set; }
    }
}

namespace Crooked.Models
{
    public sealed class ExchangeRequest
    {
        public int TransactionId { get; set; }
        public string? ProcessedBy { get; set; }
        public string? Notes { get; set; }
        public List<ExchangeLine> Items { get; set; } = new();
    }

    // Swaps Quantity units of a purchased item for another size/color of the same product.
    public sealed class ExchangeLine
    {
        public long TransactionItemId { get; set; }
        public int Quantity { get; set; }
        public int NewProductId { get; set; }
    }
}

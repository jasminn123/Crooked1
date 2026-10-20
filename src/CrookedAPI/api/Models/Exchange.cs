using Crooked.Services;

namespace Crooked.Models
{
    public sealed class ExchangeRequest
    {
        public int TransactionId { get; set; }
        public string? ProcessedBy { get; set; }
        public string? Notes { get; set; }
        public List<ExchangeLine> Items { get; set; } = new();
    }

    // Swaps one purchased item for another size/color of the same product.
    public sealed class ExchangeLine
    {
        public long TransactionItemId { get; set; }
        public int Quantity { get; set; } = 1;
        public int NewProductId { get; set; }
    }

    // Shop policy for exchanges.
    public static class ExchangePolicy
    {
        // A sale can be exchanged until the end of the third day after it (Philippine dates):
        // a sale on Oct 20 can be exchanged until Oct 23.
        public const int WindowDays = 3;

        // Only items bought as a single piece can be exchanged.
        public const int ItemQuantity = 1;

        public static DateOnly LastDay(DateTime saleUtc) => ShopClock.ShopDate(saleUtc).AddDays(WindowDays);

        public static bool IsOpen(DateTime saleUtc) => ShopClock.Today <= LastDay(saleUtc);

        // Earliest sale time that can still be exchanged today.
        public static DateTime OldestSaleUtc => ShopClock.StartOfDayUtc(ShopClock.Today.AddDays(-WindowDays));
    }
}

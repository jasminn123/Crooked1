using System.Globalization;
using Crooked.Services;

namespace Crooked.Models
{
    // Values of discounts.discount_type.
    public static class DiscountTypes
    {
        // value is a whole-number percentage from 1 to 100.
        public const string Percent = "percent";
        // value is pesos off the subtotal.
        public const string Fixed = "fixed";

        public static string? Normalize(string? type) => type?.Trim().ToLowerInvariant() switch
        {
            Percent => Percent,
            Fixed => Fixed,
            _ => null
        };
    }

    public static class DiscountStatuses
    {
        public const string Active = "Active";
        public const string Scheduled = "Scheduled";
        public const string Expired = "Expired";
        public const string UsedUp = "Used up";
        public const string Disabled = "Disabled";
    }

    // A coupon as stored in the discounts table, with the rules for using it. The POS shows the
    // same discount before the sale is saved (discountAmount in PointOfSale.js); the server works
    // it out again when the sale is saved, so the stored total never comes from the browser.
    public sealed record StoredDiscount(
        long Id,
        string Code,
        string Description,
        string Type,
        decimal Value,
        decimal MinPurchase,
        int? MaxUses,
        int TimesUsed,
        DateOnly? StartsOn,
        DateOnly? EndsOn,
        bool IsActive,
        string CreatedBy,
        DateTime CreatedAt)
    {
        // The largest amount the POS amount fields accept (5 digits and 2 decimals).
        public const decimal MaxAmount = 99999.99m;

        // today is a Philippine date (ShopClock.Today).
        public string Status(DateOnly today) =>
            !IsActive ? DiscountStatuses.Disabled
            : MaxUses is int max && TimesUsed >= max ? DiscountStatuses.UsedUp
            : EndsOn is DateOnly end && end < today ? DiscountStatuses.Expired
            : StartsOn is DateOnly start && start > today ? DiscountStatuses.Scheduled
            : DiscountStatuses.Active;

        public string Label => LabelFor(Type, Value);

        // "10% off" or "₱50.00 off".
        public static string LabelFor(string type, decimal value) => type == DiscountTypes.Percent
            ? $"{value.ToString("0", CultureInfo.InvariantCulture)}% off"
            : $"{Money.Peso(value)} off";

        public string Validity => (StartsOn, EndsOn) switch
        {
            (null, null) => "No end date",
            (DateOnly start, null) => $"From {ShopClock.DisplayDate(start)}",
            (null, DateOnly end) => $"Until {ShopClock.DisplayDate(end)}",
            (DateOnly start, DateOnly end) => $"{ShopClock.DisplayDate(start)} – {ShopClock.DisplayDate(end)}"
        };

        // Percentages round to the nearest centavo; a fixed amount never takes more than the subtotal.
        public decimal AmountOff(decimal subtotal) => Type == DiscountTypes.Percent
            ? Math.Round(subtotal * Value / 100m, 2, MidpointRounding.AwayFromZero)
            : Math.Min(Value, subtotal);

        // Why the code can't be used on a sale of this subtotal today, or null when it can.
        public string? Problem(decimal subtotal, DateOnly today) => Status(today) switch
        {
            DiscountStatuses.Disabled => $"The discount code {Code} has been turned off.",
            DiscountStatuses.UsedUp => $"The discount code {Code} has reached its usage limit.",
            DiscountStatuses.Expired => $"The discount code {Code} expired on {ShopClock.DisplayDate(EndsOn!.Value)}.",
            DiscountStatuses.Scheduled => $"The discount code {Code} can be used from {ShopClock.DisplayDate(StartsOn!.Value)}.",
            _ when subtotal < MinPurchase => $"The discount code {Code} needs a purchase of at least {Money.Peso(MinPurchase)}.",
            _ => null
        };
    }

    // The create form on the Discounts page.
    public sealed class DiscountInput
    {
        public string? Code { get; set; }
        public string? Description { get; set; }
        public string? Type { get; set; }
        public decimal Value { get; set; }
        public decimal? MinPurchase { get; set; }
        public int? MaxUses { get; set; }
        public DateOnly? StartsOn { get; set; }
        public DateOnly? EndsOn { get; set; }
    }

    // The POS checking a code against what is in the checkout.
    public sealed class DiscountApplyRequest
    {
        public string? Code { get; set; }
        public List<TransactionItemInput> Items { get; set; } = new();
    }
}

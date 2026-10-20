using System.Text.Json.Serialization;

namespace Crooked.Models
{
    public sealed record TransactionSummary(
        int TransactionId,
        string ReferenceId,
        string DateTime,
        string DateIso,
        decimal TotalAmount,
        string Status,
        string PaymentMethod,
        string SoldBy,
        int ItemCount,
        [property: JsonIgnore] string StoredStatus);

    public sealed record TransactionItemView(
        long TransactionItemId,
        int ProductId,
        string ProductName,
        string Category,
        string Size,
        string Color,
        int Quantity,
        decimal UnitPrice,
        string ExchangeReason);

    public sealed record ExchangeView(
        long ExchangeId,
        int TransactionId,
        string ReferenceId,
        string CreatedAt,
        string ProcessedBy,
        string Notes,
        List<ExchangeItemView> Items);

    public sealed record ExchangeItemView(
        string ProductName,
        string ReturnedSize,
        string ReturnedColor,
        string NewSize,
        string NewColor,
        int Quantity,
        string Reason);
}

namespace Crooked.Models
{
    public sealed record ForecastNotification(
        long Id,
        int ProductId,
        string ProductName,
        string? ImageUrl,
        int OldThreshold,
        int NewThreshold,
        string OldVelocity,
        string NewVelocity,
        DateTime CreatedAt,
        bool IsRead);

    public sealed record ForecastNotificationsResponse(
        int UnreadCount,
        IReadOnlyList<ForecastNotification> Notifications);
}

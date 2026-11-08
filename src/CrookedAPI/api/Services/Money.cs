using System.Globalization;

namespace Crooked.Services
{
    public static class Money
    {
        // "₱1,234.50", for amounts in messages and activity log entries (UI.formatPeso in the browser).
        public static string Peso(decimal amount) => $"₱{amount.ToString("N2", CultureInfo.InvariantCulture)}";
    }
}

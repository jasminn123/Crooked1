using System.Globalization;
using ClosedXML.Excel;
using Crooked.Models;

namespace Crooked.Services
{
    // Builds and reads the transactions workbook. Export, template and import share the
    // same sheet and column names, so an exported file can be imported again.
    public static class TransactionExcel
    {
        public const string ContentType = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";
        public const int MaxRows = 5000;

        private const string TransactionsSheet = "Transactions";
        private const string ItemsSheet = "Items";
        private const string PesoFormat = "\"₱\"#,##0.00";
        private const string DateFormat = "yyyy-mm-dd hh:mm AM/PM";

        private static readonly (string Name, double Width)[] TransactionColumns =
        {
            ("Transaction ID", 16), ("Reference ID", 30), ("Date & Time", 24), ("Payment Method", 18),
            ("Sold By", 22), ("Status", 14), ("Total Amount", 16)
        };

        private static readonly (string Name, double Width)[] ItemColumns =
        {
            ("Reference ID", 30), ("Product", 28), ("Category", 16), ("Size", 10), ("Color", 14),
            ("Quantity", 10), ("Unit Price", 14), ("Line Total", 14), ("Exchange Reason", 22)
        };

        private static readonly string[] TextDateFormats =
        {
            "MMM dd, yyyy · hh:mm tt", "yyyy-MM-dd hh:mm tt", "yyyy-MM-dd HH:mm", "yyyy-MM-dd HH:mm:ss", "yyyy-MM-dd",
            "M/d/yyyy h:mm tt", "M/d/yyyy H:mm", "M/d/yyyy"
        };

        public static byte[] Build(IReadOnlyList<TransactionSheetRow> transactions, IReadOnlyList<TransactionItemSheetRow> items)
        {
            using var workbook = new XLWorkbook();
            WriteTransactions(workbook, transactions);
            WriteItems(workbook, items);
            return Save(workbook);
        }

        public static byte[] BuildTemplate()
        {
            using var workbook = new XLWorkbook();
            var sheet = WriteTransactions(workbook, Array.Empty<TransactionSheetRow>());
            sheet.Cell(2, 2).Value = "GCASH-0001";
            sheet.Cell(2, 3).Value = DateTime.Today.AddHours(10);
            sheet.Cell(2, 3).Style.DateFormat.Format = DateFormat;
            sheet.Cell(2, 4).Value = PaymentMethods.GCash;
            sheet.Cell(2, 5).Value = "Staff name";
            sheet.Cell(2, 6).Value = TransactionStatuses.Completed;
            sheet.Cell(2, 7).Value = 500m;
            sheet.Cell(2, 7).Style.NumberFormat.Format = PesoFormat;

            var items = WriteItems(workbook, Array.Empty<TransactionItemSheetRow>());
            items.Cell(2, 1).Value = "GCASH-0001";
            items.Cell(2, 2).Value = "Product name";
            items.Cell(2, 3).Value = "T-Shirts";
            items.Cell(2, 4).Value = "M";
            items.Cell(2, 5).Value = "Black";
            items.Cell(2, 6).Value = 1;
            items.Cell(2, 7).Value = 500m;
            items.Cell(2, 7).Style.NumberFormat.Format = PesoFormat;

            var guide = workbook.Worksheets.Add("How to import");
            var lines = new[]
            {
                "How to import transactions",
                "",
                "1. Fill in one row per sale on the Transactions sheet. Replace or delete the example rows.",
                "2. Reference ID, Date & Time, Payment Method (Cash or GCash) and Total Amount are required.",
                "3. Status can be Completed, Exchanged, Pending or Voided. It is Completed when left empty.",
                "4. Optionally list what was sold on the Items sheet. Each item's Reference ID must match a sale,",
                "   and its Product, Size and Color must match an existing product.",
                "5. Sales whose Reference ID already exists are skipped, so importing the same file twice is safe.",
                "6. Transaction ID is ignored on import; new sales get new numbers.",
                "7. Importing adds sales to the transaction logs only. Stock levels are not changed.",
                "",
                "Only .xlsx files up to 5 MB and 5,000 sales can be imported."
            };
            for (var index = 0; index < lines.Length; index++) guide.Cell(index + 1, 1).Value = lines[index];
            guide.Cell(1, 1).Style.Font.Bold = true;
            guide.Cell(1, 1).Style.Font.FontSize = 14;
            guide.Column(1).Width = 110;

            return Save(workbook);
        }

        public static ParsedWorkbook Parse(Stream stream)
        {
            var result = new ParsedWorkbook();
            using var workbook = new XLWorkbook(stream);

            if (!workbook.Worksheets.TryGetWorksheet(TransactionsSheet, out var sheet))
            {
                result.Issues.Add(new ImportIssue(TransactionsSheet, 0,
                    $"The workbook has no \"{TransactionsSheet}\" sheet. Start from the template or an exported file."));
                return result;
            }

            var columns = ReadHeader(sheet);
            var missing = new[] { "Reference ID", "Date & Time", "Payment Method", "Total Amount" }
                .Where(name => !columns.ContainsKey(name))
                .ToList();
            if (missing.Count > 0)
            {
                result.Issues.Add(new ImportIssue(TransactionsSheet, 1, $"Missing column(s): {string.Join(", ", missing)}."));
                return result;
            }

            var lastRow = sheet.LastRowUsed()?.RowNumber() ?? 1;
            if (lastRow - 1 > MaxRows)
            {
                result.Issues.Add(new ImportIssue(TransactionsSheet, 0,
                    $"The file has {lastRow - 1:N0} rows. Import at most {MaxRows:N0} sales at a time."));
                return result;
            }

            var byReference = new Dictionary<string, ImportedTransaction>(StringComparer.OrdinalIgnoreCase);
            var rejectedReferences = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (var row = 2; row <= lastRow; row++)
            {
                if (sheet.Row(row).IsEmpty()) continue;

                string Text(string column) => columns.TryGetValue(column, out var number) ? ReadText(sheet.Cell(row, number)) : "";
                var errors = new List<string>();

                var reference = Text("Reference ID");
                if (reference.Length == 0) errors.Add("Reference ID is required.");
                else if (reference.Length > 100) errors.Add("Reference ID must be 100 characters or fewer.");
                else if (byReference.ContainsKey(reference) || rejectedReferences.Contains(reference))
                    errors.Add($"Reference ID \"{reference}\" appears more than once in the file.");

                var date = ReadDate(sheet.Cell(row, columns["Date & Time"]));
                if (date == null) errors.Add("Date & Time is missing or is not a valid date.");

                var total = ReadDecimal(sheet.Cell(row, columns["Total Amount"]));
                if (total == null || total < 0) errors.Add("Total Amount must be a number of 0 or more.");

                var paymentText = Text("Payment Method");
                var payment = PaymentMethods.Normalize(paymentText);
                if (payment == null) errors.Add($"Payment Method \"{paymentText}\" must be Cash or GCash.");

                var statusText = Text("Status");
                var status = statusText.Length == 0 ? TransactionStatuses.Completed : TransactionStatuses.Normalize(statusText);
                if (status == null) errors.Add($"Status \"{statusText}\" must be Completed, Exchanged, Pending or Voided.");

                var soldBy = Text("Sold By");
                if (soldBy.Length > 100) errors.Add("Sold By must be 100 characters or fewer.");

                if (errors.Count > 0)
                {
                    result.Issues.AddRange(errors.Select(message => new ImportIssue(TransactionsSheet, row, message)));
                    result.Rejected++;
                    // A rejected duplicate must not take the items of the row that was accepted.
                    if (reference.Length > 0 && !byReference.ContainsKey(reference)) rejectedReferences.Add(reference);
                    continue;
                }

                var transaction = new ImportedTransaction
                {
                    Row = row,
                    ReferenceId = reference,
                    DateTimeUtc = ShopClock.ToUtc(date!.Value),
                    TotalAmount = Math.Round(total!.Value, 2),
                    PaymentMethod = payment!,
                    SoldBy = soldBy.Length == 0 ? "Unknown" : soldBy,
                    Status = status == TransactionStatuses.Exchanged ? TransactionStatuses.Completed : status!,
                    HasExchange = status == TransactionStatuses.Exchanged
                };
                result.Transactions.Add(transaction);
                byReference[reference] = transaction;
            }

            if (workbook.Worksheets.TryGetWorksheet(ItemsSheet, out var itemSheet))
            {
                ParseItems(itemSheet, byReference, rejectedReferences, result);
            }

            return result;
        }

        private static void ParseItems(
            IXLWorksheet sheet,
            Dictionary<string, ImportedTransaction> byReference,
            HashSet<string> rejectedReferences,
            ParsedWorkbook result)
        {
            var columns = ReadHeader(sheet);
            var missing = new[] { "Reference ID", "Product", "Quantity", "Unit Price" }
                .Where(name => !columns.ContainsKey(name))
                .ToList();
            var lastRow = sheet.LastRowUsed()?.RowNumber() ?? 1;
            if (lastRow < 2) return;
            if (missing.Count > 0)
            {
                result.Issues.Add(new ImportIssue(ItemsSheet, 1, $"Missing column(s): {string.Join(", ", missing)}."));
                return;
            }

            var failed = new HashSet<ImportedTransaction>();
            for (var row = 2; row <= lastRow; row++)
            {
                if (sheet.Row(row).IsEmpty()) continue;

                string Text(string column) => columns.TryGetValue(column, out var number) ? ReadText(sheet.Cell(row, number)) : "";
                var reference = Text("Reference ID");
                if (rejectedReferences.Contains(reference)) continue;
                if (!byReference.TryGetValue(reference, out var transaction))
                {
                    result.Issues.Add(new ImportIssue(ItemsSheet, row,
                        reference.Length == 0
                            ? "Reference ID is required."
                            : $"No sale with Reference ID \"{reference}\" is on the Transactions sheet."));
                    continue;
                }

                var errors = new List<string>();
                var product = Text("Product");
                if (product.Length == 0) errors.Add("Product is required.");

                var quantity = ReadDecimal(sheet.Cell(row, columns["Quantity"]));
                if (quantity == null || quantity <= 0 || quantity != Math.Floor(quantity.Value) || quantity > 10000)
                    errors.Add("Quantity must be a whole number of 1 or more.");

                var unitPrice = ReadDecimal(sheet.Cell(row, columns["Unit Price"]));
                if (unitPrice == null || unitPrice < 0) errors.Add("Unit Price must be a number of 0 or more.");

                if (errors.Count > 0)
                {
                    result.Issues.AddRange(errors.Select(message => new ImportIssue(ItemsSheet, row, message)));
                    failed.Add(transaction);
                    continue;
                }

                transaction.Items.Add(new ImportedTransactionItem
                {
                    Row = row,
                    ProductName = product,
                    Category = Text("Category"),
                    Size = Text("Size"),
                    Color = Text("Color"),
                    Quantity = (int)quantity!.Value,
                    UnitPrice = Math.Round(unitPrice!.Value, 2),
                    ExchangeReason = Text("Exchange Reason")
                });
            }

            // A sale is imported whole or not at all, so one bad item keeps its sale out.
            result.Transactions.RemoveAll(failed.Contains);
            result.Rejected += failed.Count;
        }

        private static IXLWorksheet WriteTransactions(XLWorkbook workbook, IReadOnlyList<TransactionSheetRow> transactions)
        {
            var sheet = AddSheet(workbook, TransactionsSheet, TransactionColumns);
            var row = 2;
            foreach (var transaction in transactions)
            {
                sheet.Cell(row, 1).Value = transaction.TransactionId;
                sheet.Cell(row, 2).Value = transaction.ReferenceId;
                sheet.Cell(row, 3).Value = ShopClock.ToShopTime(transaction.DateTimeUtc);
                sheet.Cell(row, 3).Style.DateFormat.Format = DateFormat;
                sheet.Cell(row, 4).Value = transaction.PaymentMethod;
                sheet.Cell(row, 5).Value = transaction.SoldBy;
                sheet.Cell(row, 6).Value = transaction.Status;
                sheet.Cell(row, 7).Value = transaction.TotalAmount;
                sheet.Cell(row, 7).Style.NumberFormat.Format = PesoFormat;
                row++;
            }
            return sheet;
        }

        private static IXLWorksheet WriteItems(XLWorkbook workbook, IReadOnlyList<TransactionItemSheetRow> items)
        {
            var sheet = AddSheet(workbook, ItemsSheet, ItemColumns);
            var row = 2;
            foreach (var item in items)
            {
                sheet.Cell(row, 1).Value = item.ReferenceId;
                sheet.Cell(row, 2).Value = item.ProductName;
                sheet.Cell(row, 3).Value = item.Category;
                sheet.Cell(row, 4).Value = item.Size;
                sheet.Cell(row, 5).Value = item.Color;
                sheet.Cell(row, 6).Value = item.Quantity;
                sheet.Cell(row, 7).Value = item.UnitPrice;
                sheet.Cell(row, 8).Value = item.UnitPrice * item.Quantity;
                sheet.Cell(row, 9).Value = item.ExchangeReason;
                sheet.Range(row, 7, row, 8).Style.NumberFormat.Format = PesoFormat;
                row++;
            }
            return sheet;
        }

        private static IXLWorksheet AddSheet(XLWorkbook workbook, string name, (string Name, double Width)[] columns)
        {
            var sheet = workbook.Worksheets.Add(name);
            for (var index = 0; index < columns.Length; index++)
            {
                var cell = sheet.Cell(1, index + 1);
                cell.Value = columns[index].Name;
                sheet.Column(index + 1).Width = columns[index].Width;
            }

            var header = sheet.Range(1, 1, 1, columns.Length);
            header.Style.Font.Bold = true;
            header.Style.Font.FontColor = XLColor.White;
            header.Style.Fill.BackgroundColor = XLColor.FromHtml("#111111");
            header.SetAutoFilter();
            sheet.SheetView.FreezeRows(1);
            return sheet;
        }

        private static byte[] Save(XLWorkbook workbook)
        {
            using var stream = new MemoryStream();
            workbook.SaveAs(stream);
            return stream.ToArray();
        }

        private static Dictionary<string, int> ReadHeader(IXLWorksheet sheet)
        {
            var columns = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            foreach (var cell in sheet.Row(1).CellsUsed())
            {
                var name = ReadText(cell);
                if (name.Length > 0) columns.TryAdd(name, cell.Address.ColumnNumber);
            }
            return columns;
        }

        private static string ReadText(IXLCell cell)
        {
            var value = cell.Value;
            if (value.IsBlank) return "";
            if (value.IsText) return value.GetText().Trim();
            // Long GCash reference numbers are often typed as numbers; keep every digit.
            if (value.IsNumber) return value.GetNumber().ToString("0.##########", CultureInfo.InvariantCulture);
            return cell.GetFormattedString().Trim();
        }

        private static decimal? ReadDecimal(IXLCell cell)
        {
            var value = cell.Value;
            if (value.IsNumber) return (decimal)value.GetNumber();
            if (!value.IsText) return null;

            var text = value.GetText().Replace("₱", "").Replace("PHP", "", StringComparison.OrdinalIgnoreCase).Replace(",", "").Trim();
            return decimal.TryParse(text, NumberStyles.Number, CultureInfo.InvariantCulture, out var number) ? number : null;
        }

        // Dates are read as shop (Philippine) time.
        private static DateTime? ReadDate(IXLCell cell)
        {
            var value = cell.Value;
            if (value.IsDateTime) return value.GetDateTime();
            if (value.IsNumber)
            {
                var serial = value.GetNumber();
                return serial is > 0 and < 2958466 ? DateTime.FromOADate(serial) : null;
            }
            if (!value.IsText) return null;

            var text = value.GetText().Trim();
            if (DateTime.TryParseExact(text, TextDateFormats, CultureInfo.InvariantCulture, DateTimeStyles.AllowWhiteSpaces, out var exact))
                return exact;
            return DateTime.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.AllowWhiteSpaces, out var parsed) ? parsed : null;
        }
    }
}

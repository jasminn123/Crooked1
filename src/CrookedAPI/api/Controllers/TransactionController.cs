using Microsoft.AspNetCore.Mvc;
using Npgsql;
using Crooked.Models;
using Crooked.Services;

namespace Crooked.Controllers
{
    [Route("api/POS/[controller]")]
    [ApiController]
    public class TransactionController : ControllerBase
    {
        private const long MaxImportBytes = 5 * 1024 * 1024;
        private const int MaxExportRows = 50000;
        private readonly string _connectionString = DatabaseConfig.ConnectionString;

        [HttpGet("today-revenue")]
        public IActionResult GetTodayRevenue()
        {
            try
            {
                using (var connection = new NpgsqlConnection(_connectionString))
                {
                    connection.Open();
                    const string sql = @"
                        SELECT COALESCE(SUM(total_amount), 0)
                        FROM transactions
                        WHERE status = 'Completed'
                          AND date_time >= ((NOW() AT TIME ZONE 'Asia/Manila')::date::timestamp AT TIME ZONE 'Asia/Manila')
                          AND date_time < (((NOW() AT TIME ZONE 'Asia/Manila')::date + 1)::timestamp AT TIME ZONE 'Asia/Manila')";

                    using (var command = new NpgsqlCommand(sql, connection))
                    {
                        var revenue = Convert.ToDecimal(command.ExecuteScalar());
                        return Ok(new { revenue });
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Today's Revenue Error: {ex.Message}");
                return StatusCode(500, new { error = "Unable to load today's revenue." });
            }
        }

        [HttpPost]
        [LiveUpdate(LiveTopics.Transactions)]
        public async Task<IActionResult> SaveTransaction([FromBody] Transaction tx)
        {
            try
            {
                if (tx == null)
                {
                    Console.WriteLine("ERROR: Transaction object is null");
                    return BadRequest(new { error = "Transaction is null" });
                }

                if (tx.Items == null || tx.Items.Any(item => item.ProductId <= 0 || item.Quantity <= 0))
                {
                    return BadRequest(new { error = "Transaction items must have a valid product and quantity." });
                }

                var paymentMethod = PaymentMethods.Normalize(tx.PaymentMethod) ?? PaymentMethods.Unknown;
                Console.WriteLine($"Received: RefId={tx.ReferenceId}, Date={tx.Date_Time}, Total={tx.Total_Amount}, Status={tx.Status}, Payment={paymentMethod}");

                await using (var conn = new NpgsqlConnection(_connectionString))
                {
                    await conn.OpenAsync();
                    await using var dbTransaction = await conn.BeginTransactionAsync();
                    try
                    {
                        const string sql = @"INSERT INTO Transactions
                                           (reference_id, date_time, total_amount, status, assisted_by, payment_method)
                                           VALUES (@referenceId, @dateTime, @total, @status, @assistedBy, @paymentMethod)
                                           RETURNING transaction_id";

                        int transactionId;
                        await using (var cmd = new NpgsqlCommand(sql, conn, dbTransaction))
                        {
                            cmd.Parameters.AddWithValue("@referenceId", tx.ReferenceId ?? (object)DBNull.Value);
                            cmd.Parameters.AddWithValue("@dateTime", tx.Date_Time);
                            cmd.Parameters.AddWithValue("@total", tx.Total_Amount);
                            cmd.Parameters.AddWithValue("@status", tx.Status ?? "Completed");
                            cmd.Parameters.AddWithValue("@assistedBy",
                                string.IsNullOrWhiteSpace(tx.AssistedBy) ? "Unknown" : tx.AssistedBy.Trim());
                            cmd.Parameters.AddWithValue("@paymentMethod", paymentMethod);
                            transactionId = Convert.ToInt32(await cmd.ExecuteScalarAsync());
                        }

                        foreach (var item in tx.Items)
                        {
                            const string itemSql = @"
                                INSERT INTO transaction_items
                                    (transaction_id, product_id, product_name, category, size, color, quantity, unit_price)
                                SELECT @transaction_id, id, product_name, COALESCE(category, ''),
                                       COALESCE(size, ''), COALESCE(color, ''), @quantity, price
                                FROM products
                                WHERE id = @product_id";
                            await using var itemCommand = new NpgsqlCommand(itemSql, conn, dbTransaction);
                            itemCommand.Parameters.AddWithValue("@transaction_id", transactionId);
                            itemCommand.Parameters.AddWithValue("@product_id", item.ProductId);
                            itemCommand.Parameters.AddWithValue("@quantity", item.Quantity);
                            if (await itemCommand.ExecuteNonQueryAsync() == 0)
                            {
                                throw new InvalidOperationException($"Product {item.ProductId} does not exist.");
                            }
                        }

                        await dbTransaction.CommitAsync();
                        return Ok(new { message = "Transaction saved successfully", tx });
                    }
                    catch
                    {
                        await dbTransaction.RollbackAsync();
                        throw;
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"ERROR TYPE: {ex.GetType().Name}");
                Console.WriteLine($"ERROR MESSAGE: {ex.Message}");
                Console.WriteLine($"STACK TRACE: {ex.StackTrace}");
                return StatusCode(500, new { error = ex.Message, type = ex.GetType().Name });
            }
        }

        // Every sale, newest first (used by Sales History).
        [HttpGet]
        public IActionResult GetTransactions()
        {
            try
            {
                var transactions = new List<object>();

                using (var conn = new NpgsqlConnection(_connectionString))
                {
                    conn.Open();

                    string sql = @"SELECT transaction_id, reference_id, date_time, total_amount, status, assisted_by, has_exchange, payment_method
                                   FROM Transactions
                                   ORDER BY date_time DESC";

                    using (var cmd = new NpgsqlCommand(sql, conn))
                    using (var reader = cmd.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            transactions.Add(new
                            {
                                transaction_id = reader["transaction_id"].ToString(),
                                reference_id = reader["reference_id"].ToString(),
                                date_time = ShopClock.Display(Convert.ToDateTime(reader["date_time"])),
                                total_amount = Convert.ToDecimal(reader["total_amount"]),
                                status = reader["status"].ToString(),
                                has_exchange = Convert.ToBoolean(reader["has_exchange"]),
                                assisted_by = reader["assisted_by"].ToString(),
                                payment_method = reader["payment_method"] as string ?? PaymentMethods.Unknown
                            });
                        }
                    }
                }

                return Ok(transactions);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"ERROR: {ex.Message}");
                return StatusCode(500, new { error = ex.Message });
            }
        }

        // One page of the transaction logs, with search and filters applied.
        [HttpGet("search")]
        public async Task<IActionResult> SearchTransactions([FromQuery] TransactionQuery query)
        {
            var pageSize = Math.Clamp(query.PageSize, 1, 50);
            await using var connection = new NpgsqlConnection(_connectionString);
            await connection.OpenAsync();

            int totalItems;
            await using (var countCommand = new NpgsqlCommand { Connection = connection })
            {
                countCommand.CommandText = $"SELECT COUNT(*) FROM transactions t {BuildFilter(query, countCommand)}";
                totalItems = Convert.ToInt32(await countCommand.ExecuteScalarAsync());
            }

            var totalPages = Math.Max(1, (int)Math.Ceiling(totalItems / (double)pageSize));
            var page = Math.Clamp(query.Page, 1, totalPages);
            var items = new List<TransactionSummary>();
            await using (var command = new NpgsqlCommand { Connection = connection })
            {
                command.CommandText = $@"
                    SELECT {TransactionStore.SummaryColumns}
                    FROM transactions t
                    {BuildFilter(query, command)}
                    ORDER BY t.date_time DESC, t.transaction_id DESC
                    LIMIT @limit OFFSET @offset";
                command.Parameters.AddWithValue("@limit", pageSize);
                command.Parameters.AddWithValue("@offset", (page - 1) * pageSize);
                await using var reader = await command.ExecuteReaderAsync();
                while (await reader.ReadAsync()) items.Add(TransactionStore.ReadSummary(reader));
            }

            return Ok(new { items, page, pageSize, totalItems, totalPages });
        }

        // Read-only view of one sale: what was bought and any exchanges made on it.
        [HttpGet("{transactionId:int}/details")]
        public async Task<IActionResult> GetTransactionDetails(int transactionId)
        {
            await using var connection = new NpgsqlConnection(_connectionString);
            await connection.OpenAsync();

            var transaction = await TransactionStore.FindSummaryAsync(connection, transactionId);
            if (transaction == null) return NotFound(new { message = "Transaction was not found." });

            var items = await TransactionStore.ReadItemsAsync(connection, transactionId);
            var exchanges = await TransactionStore.ReadExchangesAsync(connection, transactionId, 50);
            var canExchange = transaction.StoredStatus == TransactionStatuses.Completed
                && ExchangePolicy.IsOpen(transaction.StoredDateTime)
                && items.Any(item => item.Quantity == ExchangePolicy.ItemQuantity);
            return Ok(new
            {
                transaction,
                items,
                exchanges,
                canExchange,
                exchangeUntil = ShopClock.DisplayDate(ExchangePolicy.LastDay(transaction.StoredDateTime))
            });
        }

        // Excel workbook of every sale that matches the current search and filters.
        [HttpGet("export")]
        public async Task<IActionResult> ExportTransactions([FromQuery] TransactionQuery query)
        {
            await using var connection = new NpgsqlConnection(_connectionString);
            await connection.OpenAsync();

            var transactions = new List<TransactionSheetRow>();
            await using (var command = new NpgsqlCommand { Connection = connection })
            {
                command.CommandText = $@"
                    SELECT t.transaction_id, t.reference_id, t.date_time, t.total_amount, t.status, t.has_exchange,
                           t.assisted_by, t.payment_method
                    FROM transactions t
                    {BuildFilter(query, command)}
                    ORDER BY t.date_time DESC, t.transaction_id DESC
                    LIMIT {MaxExportRows}";
                await using var reader = await command.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                {
                    transactions.Add(new TransactionSheetRow(
                        reader.GetInt32(0),
                        reader.IsDBNull(1) ? "" : reader.GetString(1),
                        reader.GetDateTime(2),
                        reader.IsDBNull(7) ? PaymentMethods.Unknown : reader.GetString(7),
                        reader.IsDBNull(6) ? "Unknown" : reader.GetString(6),
                        TransactionStatuses.Display(reader.IsDBNull(4) ? TransactionStatuses.Completed : reader.GetString(4), reader.GetBoolean(5)),
                        reader.GetDecimal(3)));
                }
            }

            var items = new List<TransactionItemSheetRow>();
            await using (var command = new NpgsqlCommand { Connection = connection })
            {
                command.CommandText = $@"
                    SELECT t.reference_id, ti.product_name, ti.category, ti.size, ti.color, ti.quantity, ti.unit_price, ti.exchange_reason
                    FROM transaction_items ti
                    JOIN (
                        SELECT t.transaction_id, t.reference_id, t.date_time
                        FROM transactions t
                        {BuildFilter(query, command)}
                        ORDER BY t.date_time DESC, t.transaction_id DESC
                        LIMIT {MaxExportRows}
                    ) t ON t.transaction_id = ti.transaction_id
                    ORDER BY t.date_time DESC, t.transaction_id DESC, ti.transaction_item_id";
                await using var reader = await command.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                {
                    items.Add(new TransactionItemSheetRow(
                        reader.IsDBNull(0) ? "" : reader.GetString(0),
                        reader.GetString(1),
                        reader.GetString(2),
                        reader.GetString(3),
                        reader.GetString(4),
                        reader.GetInt32(5),
                        reader.GetDecimal(6),
                        reader.GetString(7)));
                }
            }

            var fileName = $"crkd-transactions-{ShopClock.ToShopTime(DateTime.UtcNow):yyyyMMdd-HHmm}.xlsx";
            return File(TransactionExcel.Build(transactions, items), TransactionExcel.ContentType, fileName);
        }

        [HttpGet("import-template")]
        public IActionResult DownloadImportTemplate() =>
            File(TransactionExcel.BuildTemplate(), TransactionExcel.ContentType, "crkd-transactions-template.xlsx");

        // Adds sales from an Excel workbook to the logs. A sale whose reference ID already exists is
        // updated when any of its fields (or, when the file lists them, its items) differ, and skipped
        // when nothing changed. Stock levels are never changed.
        [HttpPost("import")]
        [LiveUpdate(LiveTopics.Transactions, LiveTopics.Activity)]
        public async Task<IActionResult> ImportTransactions(IFormFile? file, [FromForm] string? importedBy)
        {
            if (file == null || file.Length == 0)
                return BadRequest(new { message = "Choose an Excel file (.xlsx) to import." });
            if (!string.Equals(Path.GetExtension(file.FileName), ".xlsx", StringComparison.OrdinalIgnoreCase))
                return BadRequest(new { message = "Only Excel workbooks (.xlsx) can be imported." });
            if (file.Length > MaxImportBytes)
                return BadRequest(new { message = "The file is larger than 5 MB." });

            await using var buffer = new MemoryStream();
            await file.CopyToAsync(buffer);
            buffer.Position = 0;
            // .xlsx files are zip archives, which start with "PK\x03\x04".
            var signature = new byte[4];
            if (await buffer.ReadAsync(signature) < 4 || signature[0] != 0x50 || signature[1] != 0x4B || signature[2] != 0x03 || signature[3] != 0x04)
                return BadRequest(new { message = "This file is not a valid Excel workbook (.xlsx)." });
            buffer.Position = 0;

            ParsedWorkbook workbook;
            try
            {
                workbook = TransactionExcel.Parse(buffer);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Import Read Error: {ex.Message}");
                return BadRequest(new { message = "The file could not be read as an Excel workbook (.xlsx)." });
            }

            var issues = workbook.Issues;
            var rejected = workbook.Rejected;
            var imported = 0;
            var updated = 0;
            var skipped = 0;

            await using var connection = new NpgsqlConnection(_connectionString);
            await connection.OpenAsync();
            await using var dbTransaction = await connection.BeginTransactionAsync();
            try
            {
                // Sales already in the logs, by reference ID (the oldest one if a reference repeats).
                var existing = new Dictionary<string, StoredSale>(StringComparer.OrdinalIgnoreCase);
                await using (var command = new NpgsqlCommand(@"
                    SELECT transaction_id, reference_id, date_time, total_amount, status, has_exchange, assisted_by, payment_method
                    FROM transactions
                    WHERE lower(reference_id) = ANY(@references)
                    ORDER BY transaction_id", connection, dbTransaction))
                {
                    command.Parameters.AddWithValue("@references",
                        workbook.Transactions.Select(transaction => transaction.ReferenceId.ToLowerInvariant()).ToArray());
                    await using var reader = await command.ExecuteReaderAsync();
                    while (await reader.ReadAsync())
                    {
                        existing.TryAdd(reader.GetString(1), new StoredSale(
                            reader.GetInt32(0),
                            reader.GetDateTime(2),
                            reader.GetDecimal(3),
                            reader.IsDBNull(4) ? TransactionStatuses.Completed : reader.GetString(4),
                            reader.GetBoolean(5),
                            reader.IsDBNull(6) ? "Unknown" : reader.GetString(6),
                            reader.IsDBNull(7) ? PaymentMethods.Unknown : reader.GetString(7)));
                    }
                }

                var storedItems = existing.Values.ToDictionary(sale => sale.Id, _ => new List<string>());
                await using (var command = new NpgsqlCommand(@"
                    SELECT transaction_id, product_id, quantity, unit_price, exchange_reason
                    FROM transaction_items
                    WHERE transaction_id = ANY(@ids)", connection, dbTransaction))
                {
                    command.Parameters.AddWithValue("@ids", storedItems.Keys.ToArray());
                    await using var reader = await command.ExecuteReaderAsync();
                    while (await reader.ReadAsync())
                    {
                        storedItems[reader.GetInt32(0)].Add(
                            ItemSignature(reader.GetInt32(1), reader.GetInt32(2), reader.GetDecimal(3), reader.GetString(4)));
                    }
                }

                // Items are matched to products ignoring case, then saved with the product's own spelling.
                var products = new Dictionary<string, (int Id, bool IsActive, string Name, string Category, string Size, string Color)>();
                await using (var command = new NpgsqlCommand(@"
                    SELECT id, COALESCE(product_name, ''), COALESCE(category, ''), COALESCE(size, ''), COALESCE(color, ''), is_active
                    FROM products", connection, dbTransaction))
                await using (var reader = await command.ExecuteReaderAsync())
                {
                    while (await reader.ReadAsync())
                    {
                        (int Id, bool IsActive, string Name, string Category, string Size, string Color) product =
                            (reader.GetInt32(0), reader.GetBoolean(5), reader.GetString(1), reader.GetString(2),
                             reader.GetString(3), reader.GetString(4));
                        var key = ProductKey(product.Name, product.Size, product.Color);
                        if (!products.TryGetValue(key, out var current) || (product.IsActive && !current.IsActive))
                            products[key] = product;
                    }
                }

                async Task InsertItemsAsync(int transactionId, ImportedTransaction transaction)
                {
                    foreach (var item in transaction.Items)
                    {
                        var product = products[ProductKey(item.ProductName, item.Size, item.Color)];
                        await using var command = new NpgsqlCommand(@"
                            INSERT INTO transaction_items
                                (transaction_id, product_id, product_name, category, size, color, quantity, unit_price, exchange_reason)
                            VALUES (@transaction_id, @product_id, @product_name, @category, @size, @color, @quantity, @unit_price, @exchange_reason)",
                            connection, dbTransaction);
                        command.Parameters.AddWithValue("@transaction_id", transactionId);
                        command.Parameters.AddWithValue("@product_id", product.Id);
                        command.Parameters.AddWithValue("@product_name", product.Name);
                        command.Parameters.AddWithValue("@category", item.Category.Length > 0 ? item.Category : product.Category);
                        command.Parameters.AddWithValue("@size", product.Size);
                        command.Parameters.AddWithValue("@color", product.Color);
                        command.Parameters.AddWithValue("@quantity", item.Quantity);
                        command.Parameters.AddWithValue("@unit_price", item.UnitPrice);
                        command.Parameters.AddWithValue("@exchange_reason", item.ExchangeReason);
                        await command.ExecuteNonQueryAsync();
                    }
                }

                foreach (var transaction in workbook.Transactions)
                {
                    var unknownItems = transaction.Items
                        .Where(item => !products.ContainsKey(ProductKey(item.ProductName, item.Size, item.Color)))
                        .ToList();
                    if (unknownItems.Count > 0)
                    {
                        issues.AddRange(unknownItems.Select(item => new ImportIssue("Items", item.Row,
                            $"No product named \"{item.ProductName}\" with size \"{item.Size}\" and color \"{item.Color}\" exists.")));
                        rejected++;
                        continue;
                    }

                    if (!existing.TryGetValue(transaction.ReferenceId, out var stored))
                    {
                        int transactionId;
                        await using (var command = new NpgsqlCommand(@"
                            INSERT INTO transactions (reference_id, date_time, total_amount, status, assisted_by, payment_method, has_exchange)
                            VALUES (@reference_id, @date_time, @total_amount, @status, @assisted_by, @payment_method, @has_exchange)
                            RETURNING transaction_id", connection, dbTransaction))
                        {
                            command.Parameters.AddWithValue("@reference_id", transaction.ReferenceId);
                            command.Parameters.AddWithValue("@date_time", transaction.DateTimeUtc);
                            command.Parameters.AddWithValue("@total_amount", transaction.TotalAmount);
                            command.Parameters.AddWithValue("@status", transaction.Status);
                            command.Parameters.AddWithValue("@assisted_by", transaction.SoldBy);
                            command.Parameters.AddWithValue("@payment_method", transaction.PaymentMethod);
                            command.Parameters.AddWithValue("@has_exchange", transaction.HasExchange);
                            transactionId = Convert.ToInt32(await command.ExecuteScalarAsync());
                        }

                        await InsertItemsAsync(transactionId, transaction);
                        imported++;
                        continue;
                    }

                    // Excel keeps times to the second, so stored times are compared at that precision.
                    var fieldsChanged = WholeSeconds(stored.DateTime) != WholeSeconds(transaction.DateTimeUtc)
                        || stored.Total != transaction.TotalAmount
                        || stored.Status != transaction.Status
                        || stored.HasExchange != transaction.HasExchange
                        || stored.SoldBy != transaction.SoldBy
                        || stored.PaymentMethod != transaction.PaymentMethod;
                    // Items are only compared when the file lists some for this sale; otherwise they are kept.
                    var itemsChanged = transaction.Items.Count > 0 && !transaction.Items
                        .Select(item => ItemSignature(products[ProductKey(item.ProductName, item.Size, item.Color)].Id,
                            item.Quantity, item.UnitPrice, item.ExchangeReason))
                        .Order()
                        .SequenceEqual(storedItems[stored.Id].Order());

                    if (!fieldsChanged && !itemsChanged)
                    {
                        skipped++;
                        continue;
                    }

                    if (fieldsChanged)
                    {
                        await using var command = new NpgsqlCommand(@"
                            UPDATE transactions
                            SET date_time = @date_time, total_amount = @total_amount, status = @status,
                                has_exchange = @has_exchange, assisted_by = @assisted_by, payment_method = @payment_method
                            WHERE transaction_id = @transaction_id", connection, dbTransaction);
                        command.Parameters.AddWithValue("@date_time", transaction.DateTimeUtc);
                        command.Parameters.AddWithValue("@total_amount", transaction.TotalAmount);
                        command.Parameters.AddWithValue("@status", transaction.Status);
                        command.Parameters.AddWithValue("@has_exchange", transaction.HasExchange);
                        command.Parameters.AddWithValue("@assisted_by", transaction.SoldBy);
                        command.Parameters.AddWithValue("@payment_method", transaction.PaymentMethod);
                        command.Parameters.AddWithValue("@transaction_id", stored.Id);
                        await command.ExecuteNonQueryAsync();
                    }

                    if (itemsChanged)
                    {
                        await using (var command = new NpgsqlCommand(
                            "DELETE FROM transaction_items WHERE transaction_id = @transaction_id", connection, dbTransaction))
                        {
                            command.Parameters.AddWithValue("@transaction_id", stored.Id);
                            await command.ExecuteNonQueryAsync();
                        }
                        await InsertItemsAsync(stored.Id, transaction);
                    }

                    updated++;
                }

                if (imported + updated > 0)
                {
                    var staffName = string.IsNullOrWhiteSpace(importedBy) ? "Unknown" : importedBy.Trim();
                    var parts = new List<string>();
                    if (imported > 0) parts.Add($"imported {imported}");
                    if (updated > 0) parts.Add($"updated {updated}");
                    var action = string.Join(" and ", parts);
                    await TransactionStore.LogActivityAsync(connection, dbTransaction, staffName,
                        $"{char.ToUpper(action[0])}{action[1..]} transaction{(imported + updated == 1 ? "" : "s")} from Excel");
                }

                await dbTransaction.CommitAsync();
            }
            catch (Exception ex)
            {
                await dbTransaction.RollbackAsync();
                Console.WriteLine($"Import Error: {ex.Message}");
                return StatusCode(500, new { message = "The import could not be saved. No transactions were added or changed." });
            }

            return Ok(new
            {
                imported,
                updated,
                skipped,
                rejected,
                totalIssues = issues.Count,
                issues = issues.OrderBy(issue => issue.Sheet).ThenBy(issue => issue.Row).Take(50)
            });
        }

        private sealed record StoredSale(int Id, DateTime DateTime, decimal Total, string Status, bool HasExchange, string SoldBy, string PaymentMethod);

        private static long WholeSeconds(DateTime storedUtc) => ShopClock.ToShopTime(storedUtc).Ticks / TimeSpan.TicksPerSecond;

        private static string ItemSignature(int productId, int quantity, decimal unitPrice, string exchangeReason) =>
            string.Join("|", productId, quantity, unitPrice.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture), exchangeReason);

        private static string ProductKey(string name, string size, string color) =>
            string.Join("|", name.Trim().ToLowerInvariant(), size.Trim().ToLowerInvariant(), color.Trim().ToLowerInvariant());

        // WHERE clause for the search, status, payment and date filters; adds its parameters to command.
        private static string BuildFilter(TransactionQuery query, NpgsqlCommand command)
        {
            var conditions = new List<string>();

            var search = query.Search?.Trim();
            if (!string.IsNullOrEmpty(search))
            {
                var pattern = "%" + search.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_") + "%";
                command.Parameters.AddWithValue("@search", pattern);
                command.Parameters.AddWithValue("@search_exact", search.TrimStart('#'));
                conditions.Add(@"(t.reference_id ILIKE @search
                    OR t.assisted_by ILIKE @search
                    OR CAST(t.transaction_id AS TEXT) = @search_exact
                    OR EXISTS (SELECT 1 FROM transaction_items ti
                               WHERE ti.transaction_id = t.transaction_id AND ti.product_name ILIKE @search))");
            }

            switch (TransactionStatuses.Normalize(query.Status))
            {
                case TransactionStatuses.Exchanged:
                    conditions.Add("t.has_exchange");
                    break;
                case string status:
                    command.Parameters.AddWithValue("@status", status);
                    conditions.Add("t.status = @status AND NOT t.has_exchange");
                    break;
            }

            switch (PaymentMethods.Normalize(query.Payment))
            {
                case PaymentMethods.Unknown:
                    conditions.Add("(t.payment_method IS NULL OR t.payment_method = 'Unknown')");
                    break;
                case string method:
                    command.Parameters.AddWithValue("@payment", method);
                    conditions.Add("t.payment_method = @payment");
                    break;
            }

            if (query.From is DateOnly from)
            {
                command.Parameters.AddWithValue("@from", ShopClock.StartOfDayUtc(from));
                conditions.Add("t.date_time >= @from");
            }

            if (query.To is DateOnly to)
            {
                command.Parameters.AddWithValue("@to", ShopClock.StartOfDayUtc(to.AddDays(1)));
                conditions.Add("t.date_time < @to");
            }

            return conditions.Count == 0 ? "" : "WHERE " + string.Join(" AND ", conditions);
        }
    }
}

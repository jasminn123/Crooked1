using Microsoft.AspNetCore.Mvc;
using Npgsql;
using Crooked.Models;
using System;

namespace Crooked.Controllers
{
    [Route("api/POS/[controller]")]
    [ApiController]
    public class TransactionController : ControllerBase
    {
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

                Console.WriteLine($"Received: RefId={tx.ReferenceId}, Date={tx.Date_Time}, Total={tx.Total_Amount}, Status={tx.Status}");

                await using (var conn = new NpgsqlConnection(_connectionString))
                {
                    await conn.OpenAsync();
                    await using var dbTransaction = await conn.BeginTransactionAsync();
                    try
                    {
                        const string sql = @"INSERT INTO Transactions
                                           (reference_id, date_time, total_amount, status, assisted_by)
                                           VALUES (@referenceId, @dateTime, @total, @status, @assistedBy)
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

        [HttpGet("{transactionId:int}/items")]
        public async Task<IActionResult> GetTransactionItems(int transactionId)
        {
            await using var connection = new NpgsqlConnection(_connectionString);
            await connection.OpenAsync();

            const string itemsSql = @"
                SELECT ti.transaction_item_id, ti.product_id, ti.product_name, ti.category,
                       ti.size, ti.color, ti.quantity, ti.unit_price, ti.exchange_reason
                FROM transaction_items ti
                JOIN transactions t ON t.transaction_id = ti.transaction_id
                WHERE t.transaction_id = @transaction_id AND t.status = 'Completed'
                ORDER BY ti.transaction_item_id";

            var items = new List<object>();
            await using (var command = new NpgsqlCommand(itemsSql, connection))
            {
                command.Parameters.AddWithValue("@transaction_id", transactionId);
                await using var reader = await command.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                {
                    items.Add(new
                    {
                        transactionItemId = reader.GetInt64(0),
                        productId = reader.GetInt32(1),
                        productName = reader.GetString(2),
                        category = reader.GetString(3),
                        size = reader.GetString(4),
                        color = reader.GetString(5),
                        quantity = reader.GetInt32(6),
                        unitPrice = reader.GetDecimal(7),
                        exchangeReason = reader.GetString(8)
                    });
                }
            }

            if (items.Count == 0)
            {
                const string existsSql = "SELECT EXISTS (SELECT 1 FROM transactions WHERE transaction_id = @transaction_id AND status = 'Completed')";
                await using var existsCommand = new NpgsqlCommand(existsSql, connection);
                existsCommand.Parameters.AddWithValue("@transaction_id", transactionId);
                if (!Convert.ToBoolean(await existsCommand.ExecuteScalarAsync()))
                {
                    return NotFound(new { message = "Completed transaction was not found." });
                }

                return Conflict(new { message = "This transaction has no saved item details and cannot be edited." });
            }

            const string productsSql = @"
                SELECT id, product_name, category, size, color, price, stock_quantity
                FROM products
                WHERE is_active = TRUE
                ORDER BY product_name, size, color";
            var variants = new List<object>();
            await using (var command = new NpgsqlCommand(productsSql, connection))
            await using (var reader = await command.ExecuteReaderAsync())
            {
                while (await reader.ReadAsync())
                {
                    variants.Add(new
                    {
                        id = reader.GetInt32(0),
                        productName = reader.IsDBNull(1) ? "" : reader.GetString(1),
                        category = reader.IsDBNull(2) ? "" : reader.GetString(2),
                        size = reader.IsDBNull(3) ? "" : reader.GetString(3),
                        color = reader.IsDBNull(4) ? "" : reader.GetString(4),
                        price = reader.GetDecimal(5),
                        stockQuantity = reader.GetInt32(6)
                    });
                }
            }

            return Ok(new { items, variants });
        }

        [HttpPut("{transactionId:int}/items")]
        public async Task<IActionResult> ExchangeTransactionItems(
            int transactionId,
            [FromBody] ExchangeTransactionRequest request)
        {
            var allowedReasons = new HashSet<string>(StringComparer.Ordinal)
            {
                "Change Color",
                "Change Size",
                "Damaged"
            };
            if (request?.Items == null || request.Items.Count == 0
                || request.Items.Any(item => item.TransactionItemId <= 0
                    || item.ProductId <= 0
                    || (!string.IsNullOrWhiteSpace(item.Reason)
                        && !allowedReasons.Contains(item.Reason))))
            {
                return BadRequest(new { message = "Provide each purchased unit with a valid product and exchange reason." });
            }

            await using var connection = new NpgsqlConnection(_connectionString);
            await connection.OpenAsync();
            await using var transaction = await connection.BeginTransactionAsync();
            try
            {
                const string transactionSql = @"
                    SELECT (date_time AT TIME ZONE 'Asia/Manila')::date
                    FROM transactions
                    WHERE transaction_id = @transaction_id AND status = 'Completed'
                    FOR UPDATE";
                DateTime saleDate;
                await using (var command = new NpgsqlCommand(transactionSql, connection, transaction))
                {
                    command.Parameters.AddWithValue("@transaction_id", transactionId);
                    var result = await command.ExecuteScalarAsync();
                    if (result == null)
                    {
                        await transaction.RollbackAsync();
                        return NotFound(new { message = "Completed transaction was not found." });
                    }
                    saleDate = Convert.ToDateTime(result).Date;
                }

                var originals = new Dictionary<long, (int ProductId, string ProductName, string Category, string Size, string Color, int Quantity, decimal UnitPrice)>();
                const string originalSql = @"
                    SELECT transaction_item_id, product_id, product_name, category, size, color, quantity, unit_price
                    FROM transaction_items
                    WHERE transaction_id = @transaction_id
                    FOR UPDATE";
                await using (var command = new NpgsqlCommand(originalSql, connection, transaction))
                {
                    command.Parameters.AddWithValue("@transaction_id", transactionId);
                    await using var reader = await command.ExecuteReaderAsync();
                    while (await reader.ReadAsync())
                    {
                        originals.Add(reader.GetInt64(0),
                            (reader.GetInt32(1), reader.GetString(2), reader.GetString(3),
                             reader.GetString(4), reader.GetString(5), reader.GetInt32(6), reader.GetDecimal(7)));
                    }
                }

                if (originals.Count == 0)
                {
                    await transaction.RollbackAsync();
                    return Conflict(new { message = "This transaction has no saved item details and cannot be edited." });
                }

                var requestedCounts = request.Items
                    .GroupBy(item => item.TransactionItemId)
                    .ToDictionary(group => group.Key, group => group.Count());
                if (originals.Count != requestedCounts.Count
                    || originals.Any(original => !requestedCounts.TryGetValue(original.Key, out var count)
                        || count != original.Value.Quantity))
                {
                    await transaction.RollbackAsync();
                    return BadRequest(new { message = "Submit exactly one row for every purchased unit." });
                }

                var selectedVariants = new Dictionary<int, (string ProductName, string Category, string Size, string Color, decimal Price)>();
                foreach (var item in request.Items)
                {
                    var original = originals[item.TransactionItemId];
                    if (!selectedVariants.TryGetValue(item.ProductId, out var selectedVariant))
                    {
                        const string variantSql = @"
                            SELECT product_name, category, size, color, price
                            FROM products
                            WHERE id = @product_id AND is_active = TRUE
                            FOR UPDATE";
                        await using (var command = new NpgsqlCommand(variantSql, connection, transaction))
                        {
                            command.Parameters.AddWithValue("@product_id", item.ProductId);
                            await using var reader = await command.ExecuteReaderAsync();
                            if (!await reader.ReadAsync())
                            {
                                await transaction.RollbackAsync();
                                return Conflict(new { message = "A selected product variant is no longer available." });
                            }
                            selectedVariant = (
                                reader.IsDBNull(0) ? "" : reader.GetString(0),
                                reader.IsDBNull(1) ? "" : reader.GetString(1),
                                reader.IsDBNull(2) ? "" : reader.GetString(2),
                                reader.IsDBNull(3) ? "" : reader.GetString(3),
                                reader.GetDecimal(4));
                        }

                        selectedVariants.Add(item.ProductId, selectedVariant);
                    }

                    if (!string.Equals(selectedVariant.ProductName, original.ProductName, StringComparison.OrdinalIgnoreCase)
                        || !string.Equals(selectedVariant.Category, original.Category, StringComparison.OrdinalIgnoreCase)
                        || selectedVariant.Price != original.UnitPrice)
                    {
                        await transaction.RollbackAsync();
                        return BadRequest(new { message = "Exchanges are limited to variants of the same product at the original price." });
                    }
                }

                var hasExchange = request.Items.Any(item =>
                {
                    var original = originals[item.TransactionItemId];
                    var replacement = selectedVariants[item.ProductId];
                    return item.ProductId != original.ProductId
                        || !string.Equals(replacement.Size, original.Size, StringComparison.OrdinalIgnoreCase)
                        || !string.Equals(replacement.Color, original.Color, StringComparison.OrdinalIgnoreCase);
                });

                foreach (var original in originals)
                {
                    var returnedQuantity = request.Items.Count(item =>
                        item.TransactionItemId == original.Key
                        && item.ProductId != original.Value.ProductId
                        && item.Reason != "Damaged");
                    if (returnedQuantity > 0)
                    {
                        await using (var returnStock = new NpgsqlCommand(
                            "UPDATE products SET stock_quantity = stock_quantity + @quantity WHERE id = @product_id",
                            connection, transaction))
                        {
                            returnStock.Parameters.AddWithValue("@quantity", returnedQuantity);
                            returnStock.Parameters.AddWithValue("@product_id", original.Value.ProductId);
                            await returnStock.ExecuteNonQueryAsync();
                        }
                    }
                }

                foreach (var replacement in request.Items
                    .Where(item => item.ProductId != originals[item.TransactionItemId].ProductId || item.Reason == "Damaged")
                    .GroupBy(item => item.ProductId))
                {
                    await using var takeStock = new NpgsqlCommand(
                        @"UPDATE products
                          SET stock_quantity = stock_quantity - @quantity
                          WHERE id = @product_id AND is_active = TRUE AND stock_quantity >= @quantity",
                        connection, transaction);
                    takeStock.Parameters.AddWithValue("@quantity", replacement.Count());
                    takeStock.Parameters.AddWithValue("@product_id", replacement.Key);
                    if (await takeStock.ExecuteNonQueryAsync() == 0)
                    {
                        await transaction.RollbackAsync();
                        return Conflict(new { message = "The selected variant does not have enough stock." });
                    }
                }

                foreach (var original in originals.GroupBy(item => item.Value.ProductId))
                {
                    var quantity = original.Sum(item => item.Value.Quantity);
                    await using var subtractSales = new NpgsqlCommand(
                        @"UPDATE product_daily_sales
                          SET quantity_sold = GREATEST(quantity_sold - @quantity, 0)
                          WHERE product_id = @product_id AND sale_date = @sale_date",
                        connection, transaction);
                    subtractSales.Parameters.AddWithValue("@quantity", quantity);
                    subtractSales.Parameters.AddWithValue("@product_id", original.Key);
                    subtractSales.Parameters.AddWithValue("@sale_date", saleDate);
                    await subtractSales.ExecuteNonQueryAsync();
                }

                foreach (var replacement in request.Items.GroupBy(item => item.ProductId))
                {
                    await using var addSales = new NpgsqlCommand(
                        @"INSERT INTO product_daily_sales (product_id, sale_date, quantity_sold)
                          VALUES (@product_id, @sale_date, @quantity)
                          ON CONFLICT (product_id, sale_date)
                          DO UPDATE SET quantity_sold = product_daily_sales.quantity_sold + EXCLUDED.quantity_sold",
                        connection, transaction);
                    addSales.Parameters.AddWithValue("@product_id", replacement.Key);
                    addSales.Parameters.AddWithValue("@sale_date", saleDate);
                    addSales.Parameters.AddWithValue("@quantity", replacement.Count());
                    await addSales.ExecuteNonQueryAsync();
                }

                await using (var deleteItems = new NpgsqlCommand(
                    "DELETE FROM transaction_items WHERE transaction_id = @transaction_id",
                    connection, transaction))
                {
                    deleteItems.Parameters.AddWithValue("@transaction_id", transactionId);
                    await deleteItems.ExecuteNonQueryAsync();
                }

                foreach (var item in request.Items)
                {
                    var variant = selectedVariants[item.ProductId];
                    await using var insertItem = new NpgsqlCommand(
                        @"INSERT INTO transaction_items
                              (transaction_id, product_id, product_name, category, size, color, quantity, unit_price, exchange_reason)
                          VALUES
                              (@transaction_id, @product_id, @product_name, @category, @size, @color, 1, @unit_price, @exchange_reason)",
                        connection, transaction);
                    insertItem.Parameters.AddWithValue("@transaction_id", transactionId);
                    insertItem.Parameters.AddWithValue("@product_id", item.ProductId);
                    insertItem.Parameters.AddWithValue("@product_name", variant.ProductName);
                    insertItem.Parameters.AddWithValue("@category", variant.Category);
                    insertItem.Parameters.AddWithValue("@size", variant.Size);
                    insertItem.Parameters.AddWithValue("@color", variant.Color);
                    insertItem.Parameters.AddWithValue("@unit_price", variant.Price);
                    insertItem.Parameters.AddWithValue("@exchange_reason", item.Reason ?? "");
                    await insertItem.ExecuteNonQueryAsync();
                }

                await using (var updateTotal = new NpgsqlCommand(
                    @"UPDATE transactions
                      SET total_amount = (
                          SELECT COALESCE(SUM(quantity * unit_price), 0)
                          FROM transaction_items
                          WHERE transaction_id = @transaction_id
                      )
                      , has_exchange = has_exchange OR @has_exchange
                      WHERE transaction_id = @transaction_id
                      RETURNING total_amount",
                    connection, transaction))
                {
                    updateTotal.Parameters.AddWithValue("@transaction_id", transactionId);
                    updateTotal.Parameters.AddWithValue("@has_exchange", hasExchange);
                    var total = Convert.ToDecimal(await updateTotal.ExecuteScalarAsync());
                    await transaction.CommitAsync();
                    return Ok(new { totalAmount = total });
                }
            }
            catch
            {
                await transaction.RollbackAsync();
                throw;
            }
        }

        [HttpGet]
        public IActionResult GetTransactions()
        {
            try
            {
                var transactions = new List<object>();

                using (var conn = new NpgsqlConnection(_connectionString))
                {
                    conn.Open();

                    string sql = @"SELECT transaction_id, reference_id, date_time, total_amount, status, assisted_by, has_exchange
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
                                date_time = DateTime.SpecifyKind(Convert.ToDateTime(reader["date_time"]), DateTimeKind.Utc).ToLocalTime().ToString("MMM dd, yyyy · hh:mm tt"),
                                total_amount = Convert.ToDecimal(reader["total_amount"]),
                                status = reader["status"].ToString(),
                                has_exchange = Convert.ToBoolean(reader["has_exchange"]),
                                assisted_by = reader["assisted_by"].ToString()
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
    }
}
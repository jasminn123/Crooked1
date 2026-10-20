using Microsoft.AspNetCore.Mvc;
using Npgsql;
using Crooked.Models;
using Crooked.Services;

namespace Crooked.Controllers
{
    // Shop policy (ExchangePolicy): an item bought as a single piece can be exchanged within 3 days
    // for the same product in a different size and/or color at the same price. The returned item
    // goes back into stock and the replacement is taken out of stock.
    [ApiController]
    [Route("api/[controller]")]
    public class ExchangeController : ControllerBase
    {
        private readonly string _connectionString = DatabaseConfig.ConnectionString;

        // Completed sales still inside the exchange window that have at least one single-piece item.
        [HttpGet("sales")]
        public async Task<IActionResult> EligibleSales([FromQuery] int page = 1, [FromQuery] int pageSize = 5)
        {
            pageSize = Math.Clamp(pageSize, 1, 50);
            const string filter = @"
                FROM transactions t
                WHERE t.status = 'Completed'
                  AND t.date_time >= @oldest
                  AND EXISTS (SELECT 1 FROM transaction_items ti
                              WHERE ti.transaction_id = t.transaction_id AND ti.quantity = @item_quantity)";

            await using var connection = new NpgsqlConnection(_connectionString);
            await connection.OpenAsync();

            int totalItems;
            await using (var command = new NpgsqlCommand($"SELECT COUNT(*) {filter}", connection))
            {
                command.Parameters.AddWithValue("@oldest", ExchangePolicy.OldestSaleUtc);
                command.Parameters.AddWithValue("@item_quantity", ExchangePolicy.ItemQuantity);
                totalItems = Convert.ToInt32(await command.ExecuteScalarAsync());
            }

            var totalPages = Math.Max(1, (int)Math.Ceiling(totalItems / (double)pageSize));
            page = Math.Clamp(page, 1, totalPages);
            var sales = new List<object>();
            await using (var command = new NpgsqlCommand($@"
                SELECT {TransactionStore.SummaryColumns}
                {filter}
                ORDER BY t.date_time DESC, t.transaction_id DESC
                LIMIT @limit OFFSET @offset", connection))
            {
                command.Parameters.AddWithValue("@oldest", ExchangePolicy.OldestSaleUtc);
                command.Parameters.AddWithValue("@item_quantity", ExchangePolicy.ItemQuantity);
                command.Parameters.AddWithValue("@limit", pageSize);
                command.Parameters.AddWithValue("@offset", (page - 1) * pageSize);
                await using var reader = await command.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                {
                    var sale = TransactionStore.ReadSummary(reader);
                    sales.Add(new
                    {
                        sale.TransactionId,
                        sale.ReferenceId,
                        sale.DateTime,
                        sale.TotalAmount,
                        sale.Status,
                        exchangeUntil = ShopClock.DisplayDate(ExchangePolicy.LastDay(sale.StoredDateTime))
                    });
                }
            }

            return Ok(new { items = sales, page, pageSize, totalItems, totalPages, windowDays = ExchangePolicy.WindowDays });
        }

        [HttpGet("lookup")]
        public async Task<IActionResult> Lookup([FromQuery] string? query)
        {
            var text = query?.Trim().TrimStart('#') ?? "";
            if (text.Length == 0) return BadRequest(new { message = "Enter a reference ID or transaction number." });

            await using var connection = new NpgsqlConnection(_connectionString);
            await connection.OpenAsync();

            TransactionSummary? transaction;
            await using (var command = new NpgsqlCommand($@"
                SELECT {TransactionStore.SummaryColumns}
                FROM transactions t
                WHERE lower(t.reference_id) = lower(@query) OR CAST(t.transaction_id AS TEXT) = @query
                ORDER BY (lower(t.reference_id) = lower(@query)) DESC, t.date_time DESC
                LIMIT 1", connection))
            {
                command.Parameters.AddWithValue("@query", text);
                await using var reader = await command.ExecuteReaderAsync();
                transaction = await reader.ReadAsync() ? TransactionStore.ReadSummary(reader) : null;
            }

            if (transaction == null) return NotFound(new { message = $"No sale was found for \"{text}\"." });
            if (transaction.StoredStatus != TransactionStatuses.Completed)
                return Conflict(new { message = $"Only completed sales can be exchanged. This sale is {transaction.StoredStatus}." });
            if (!ExchangePolicy.IsOpen(transaction.StoredDateTime))
                return Conflict(new { message = WindowClosedMessage(transaction.StoredDateTime) });

            var allItems = await TransactionStore.ReadItemsAsync(connection, transaction.TransactionId);
            var items = allItems.Where(item => item.Quantity == ExchangePolicy.ItemQuantity).ToList();
            if (items.Count == 0)
            {
                return Conflict(new
                {
                    message = allItems.Count == 0
                        ? "This sale has no saved item details, so its items can't be exchanged."
                        : "None of the items in this sale can be exchanged. Only items bought as a single piece can be exchanged."
                });
            }

            var variants = new List<(int Id, string Name, string Category, string Size, string Color, decimal Price, int Stock)>();
            await using (var command = new NpgsqlCommand(@"
                SELECT id, product_name, COALESCE(category, ''), COALESCE(size, ''), COALESCE(color, ''), price, stock_quantity
                FROM products
                WHERE is_active = TRUE
                  AND lower(btrim(product_name)) = ANY(@names)
                ORDER BY product_name, size, color", connection))
            {
                command.Parameters.AddWithValue("@names", items.Select(item => Normalize(item.ProductName)).Distinct().ToArray());
                await using var reader = await command.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                {
                    variants.Add((reader.GetInt32(0), reader.IsDBNull(1) ? "" : reader.GetString(1), reader.GetString(2),
                        reader.GetString(3), reader.GetString(4), reader.GetDecimal(5), reader.GetInt32(6)));
                }
            }

            var exchangeItems = items.Select(item => new
            {
                item.TransactionItemId,
                item.ProductId,
                item.ProductName,
                item.Category,
                item.Size,
                item.Color,
                item.Quantity,
                item.UnitPrice,
                item.ExchangeReason,
                options = variants
                    .Where(variant => Normalize(variant.Name) == Normalize(item.ProductName)
                        && Normalize(variant.Category) == Normalize(item.Category)
                        && variant.Price == item.UnitPrice
                        && !(Normalize(variant.Size) == Normalize(item.Size) && Normalize(variant.Color) == Normalize(item.Color)))
                    .Select(variant => new { productId = variant.Id, size = variant.Size, color = variant.Color, stock = variant.Stock })
                    .ToList()
            });

            var exchanges = await TransactionStore.ReadExchangesAsync(connection, transaction.TransactionId, 50);
            return Ok(new
            {
                transaction,
                exchangeUntil = ShopClock.DisplayDate(ExchangePolicy.LastDay(transaction.StoredDateTime)),
                hiddenItemCount = allItems.Count - items.Count,
                items = exchangeItems,
                exchanges
            });
        }

        [HttpGet("recent")]
        public async Task<IActionResult> Recent([FromQuery] int limit = 10)
        {
            await using var connection = new NpgsqlConnection(_connectionString);
            await connection.OpenAsync();
            return Ok(await TransactionStore.ReadExchangesAsync(connection, null, Math.Clamp(limit, 1, 50)));
        }

        [HttpPost]
        [LiveUpdate(LiveTopics.Transactions, LiveTopics.Exchanges, LiveTopics.Products, LiveTopics.Activity)]
        public async Task<IActionResult> CreateExchange([FromBody] ExchangeRequest request)
        {
            if (request == null || request.TransactionId <= 0 || request.Items == null || request.Items.Count == 0)
                return BadRequest(new { message = "Choose at least one item to exchange." });
            if (request.Items.Count > 100 || request.Items.Any(line => line.TransactionItemId <= 0 || line.NewProductId <= 0))
                return BadRequest(new { message = "Every exchanged item needs a new size or color." });
            if (request.Items.Any(line => line.Quantity != ExchangePolicy.ItemQuantity)
                || request.Items.GroupBy(line => line.TransactionItemId).Any(group => group.Count() > 1))
                return BadRequest(new { message = "Each item is exchanged one piece at a time." });

            var processedBy = string.IsNullOrWhiteSpace(request.ProcessedBy) ? "Unknown" : request.ProcessedBy.Trim();
            if (processedBy.Length > 100) processedBy = processedBy[..100];
            var notes = request.Notes?.Trim() ?? "";
            if (notes.Length > 500) return BadRequest(new { message = "Notes must be 500 characters or fewer." });

            await using var connection = new NpgsqlConnection(_connectionString);
            await connection.OpenAsync();
            await using var transaction = await connection.BeginTransactionAsync();
            try
            {
                var referenceId = $"#{request.TransactionId}";
                var status = "";
                var saleTime = default(DateTime);
                var found = false;
                await using (var command = new NpgsqlCommand(
                    "SELECT reference_id, status, date_time FROM transactions WHERE transaction_id = @id FOR UPDATE",
                    connection, transaction))
                {
                    command.Parameters.AddWithValue("@id", request.TransactionId);
                    await using var reader = await command.ExecuteReaderAsync();
                    if (await reader.ReadAsync())
                    {
                        found = true;
                        if (!reader.IsDBNull(0)) referenceId = reader.GetString(0);
                        status = reader.IsDBNull(1) ? "" : reader.GetString(1);
                        saleTime = reader.GetDateTime(2);
                    }
                }

                if (!found)
                    return await Fail(transaction, NotFound(new { message = "The sale was not found." }));
                if (status != TransactionStatuses.Completed)
                    return await Fail(transaction, Conflict(new { message = $"Only completed sales can be exchanged. This sale is {status}." }));
                if (!ExchangePolicy.IsOpen(saleTime))
                    return await Fail(transaction, Conflict(new { message = WindowClosedMessage(saleTime) }));

                var items = new Dictionary<long, (int ProductId, string Name, string Category, string Size, string Color, int Quantity, decimal UnitPrice)>();
                await using (var command = new NpgsqlCommand(@"
                    SELECT transaction_item_id, product_id, product_name, category, size, color, quantity, unit_price
                    FROM transaction_items
                    WHERE transaction_id = @id
                    ORDER BY transaction_item_id
                    FOR UPDATE", connection, transaction))
                {
                    command.Parameters.AddWithValue("@id", request.TransactionId);
                    await using var reader = await command.ExecuteReaderAsync();
                    while (await reader.ReadAsync())
                    {
                        items.Add(reader.GetInt64(0), (reader.GetInt32(1), reader.GetString(2), reader.GetString(3),
                            reader.GetString(4), reader.GetString(5), reader.GetInt32(6), reader.GetDecimal(7)));
                    }
                }

                foreach (var line in request.Items)
                {
                    if (!items.TryGetValue(line.TransactionItemId, out var item))
                        return await Fail(transaction, BadRequest(new { message = "An item you chose is not part of this sale. Reload the sale and try again." }));
                    if (item.Quantity != ExchangePolicy.ItemQuantity)
                        return await Fail(transaction, BadRequest(new { message = $"{item.Name} was bought {item.Quantity} at a time. Only items bought as a single piece can be exchanged." }));
                }

                // Lock every product involved in a fixed order so concurrent exchanges can't deadlock.
                var productIds = request.Items.Select(line => line.NewProductId)
                    .Concat(request.Items.Select(line => items[line.TransactionItemId].ProductId))
                    .Distinct()
                    .OrderBy(id => id)
                    .ToArray();
                var products = new Dictionary<int, (string Name, string Category, string Size, string Color, decimal Price, int Stock, bool IsActive)>();
                await using (var command = new NpgsqlCommand(@"
                    SELECT id, product_name, COALESCE(category, ''), COALESCE(size, ''), COALESCE(color, ''), price, stock_quantity, is_active
                    FROM products
                    WHERE id = ANY(@ids)
                    ORDER BY id
                    FOR UPDATE", connection, transaction))
                {
                    command.Parameters.AddWithValue("@ids", productIds);
                    await using var reader = await command.ExecuteReaderAsync();
                    while (await reader.ReadAsync())
                    {
                        products.Add(reader.GetInt32(0), (reader.IsDBNull(1) ? "" : reader.GetString(1), reader.GetString(2),
                            reader.GetString(3), reader.GetString(4), reader.GetDecimal(5), reader.GetInt32(6), reader.GetBoolean(7)));
                    }
                }

                var stockChanges = new Dictionary<int, int>();
                foreach (var line in request.Items)
                {
                    var item = items[line.TransactionItemId];
                    if (!products.TryGetValue(line.NewProductId, out var product) || !product.IsActive)
                        return await Fail(transaction, Conflict(new { message = "A size or color you chose is no longer available. Reload the sale and try again." }));
                    if (Normalize(product.Name) != Normalize(item.Name) || Normalize(product.Category) != Normalize(item.Category))
                        return await Fail(transaction, BadRequest(new { message = $"{item.Name} can only be exchanged for the same product in another size or color." }));
                    if (product.Price != item.UnitPrice)
                        return await Fail(transaction, BadRequest(new { message = $"{item.Name} ({Label(product.Size, product.Color)}) has a different price, so it can't be exchanged." }));
                    if (Normalize(product.Size) == Normalize(item.Size) && Normalize(product.Color) == Normalize(item.Color))
                        return await Fail(transaction, BadRequest(new { message = $"Choose a different size or color for {item.Name}." }));

                    stockChanges[item.ProductId] = stockChanges.GetValueOrDefault(item.ProductId) + 1;
                    stockChanges[line.NewProductId] = stockChanges.GetValueOrDefault(line.NewProductId) - 1;
                }

                foreach (var (productId, change) in stockChanges)
                {
                    var product = products[productId];
                    if (product.Stock + change < 0)
                        return await Fail(transaction, Conflict(new
                        {
                            message = $"Only {product.Stock} of {product.Name} ({Label(product.Size, product.Color)}) left in stock."
                        }));
                }

                foreach (var (productId, change) in stockChanges.Where(entry => entry.Value != 0))
                {
                    await using var command = new NpgsqlCommand(
                        "UPDATE products SET stock_quantity = stock_quantity + @change WHERE id = @id", connection, transaction);
                    command.Parameters.AddWithValue("@change", change);
                    command.Parameters.AddWithValue("@id", productId);
                    await command.ExecuteNonQueryAsync();
                }

                // Forecasting counts sales per product and day, so the sale moves to the new variant.
                var saleDate = ShopClock.ShopDate(saleTime);
                foreach (var (productId, change) in stockChanges.Where(entry => entry.Value != 0))
                {
                    await using var command = new NpgsqlCommand(change > 0
                        ? @"UPDATE product_daily_sales
                            SET quantity_sold = GREATEST(quantity_sold - @quantity, 0)
                            WHERE product_id = @id AND sale_date = @sale_date"
                        : @"INSERT INTO product_daily_sales (product_id, sale_date, quantity_sold)
                            VALUES (@id, @sale_date, @quantity)
                            ON CONFLICT (product_id, sale_date)
                            DO UPDATE SET quantity_sold = product_daily_sales.quantity_sold + EXCLUDED.quantity_sold",
                        connection, transaction);
                    command.Parameters.AddWithValue("@id", productId);
                    command.Parameters.AddWithValue("@sale_date", saleDate);
                    command.Parameters.AddWithValue("@quantity", Math.Abs(change));
                    await command.ExecuteNonQueryAsync();
                }

                long exchangeId;
                await using (var command = new NpgsqlCommand(@"
                    INSERT INTO exchanges (transaction_id, processed_by, notes)
                    VALUES (@transaction_id, @processed_by, @notes)
                    RETURNING exchange_id", connection, transaction))
                {
                    command.Parameters.AddWithValue("@transaction_id", request.TransactionId);
                    command.Parameters.AddWithValue("@processed_by", processedBy);
                    command.Parameters.AddWithValue("@notes", notes);
                    exchangeId = Convert.ToInt64(await command.ExecuteScalarAsync());
                }

                var returned = new List<object>();
                var issued = new List<object>();
                foreach (var line in request.Items)
                {
                    var item = items[line.TransactionItemId];
                    var product = products[line.NewProductId];
                    var reason = ExchangeReason(item.Size, item.Color, product.Size, product.Color);

                    // The sale's item list shows what the customer has now.
                    await using (var command = new NpgsqlCommand(@"
                        UPDATE transaction_items
                        SET product_id = @product_id, size = @size, color = @color, exchange_reason = @reason
                        WHERE transaction_item_id = @item_id", connection, transaction))
                    {
                        command.Parameters.AddWithValue("@product_id", line.NewProductId);
                        command.Parameters.AddWithValue("@size", product.Size);
                        command.Parameters.AddWithValue("@color", product.Color);
                        command.Parameters.AddWithValue("@reason", reason);
                        command.Parameters.AddWithValue("@item_id", line.TransactionItemId);
                        await command.ExecuteNonQueryAsync();
                    }

                    await using (var command = new NpgsqlCommand(@"
                        INSERT INTO exchange_items
                            (exchange_id, returned_product_id, returned_product_name, returned_size, returned_color,
                             new_product_id, new_size, new_color, quantity, unit_price, reason)
                        VALUES
                            (@exchange_id, @returned_product_id, @returned_product_name, @returned_size, @returned_color,
                             @new_product_id, @new_size, @new_color, 1, @unit_price, @reason)", connection, transaction))
                    {
                        command.Parameters.AddWithValue("@exchange_id", exchangeId);
                        command.Parameters.AddWithValue("@returned_product_id", item.ProductId);
                        command.Parameters.AddWithValue("@returned_product_name", item.Name);
                        command.Parameters.AddWithValue("@returned_size", item.Size);
                        command.Parameters.AddWithValue("@returned_color", item.Color);
                        command.Parameters.AddWithValue("@new_product_id", line.NewProductId);
                        command.Parameters.AddWithValue("@new_size", product.Size);
                        command.Parameters.AddWithValue("@new_color", product.Color);
                        command.Parameters.AddWithValue("@unit_price", item.UnitPrice);
                        command.Parameters.AddWithValue("@reason", reason);
                        await command.ExecuteNonQueryAsync();
                    }

                    returned.Add(new
                    {
                        productName = item.Name,
                        size = item.Size,
                        color = item.Color,
                        quantity = 1,
                        stockAfter = products[item.ProductId].Stock + stockChanges[item.ProductId]
                    });
                    issued.Add(new
                    {
                        productName = item.Name,
                        size = product.Size,
                        color = product.Color,
                        quantity = 1,
                        stockAfter = product.Stock + stockChanges[line.NewProductId]
                    });
                }

                await using (var command = new NpgsqlCommand(
                    "UPDATE transactions SET has_exchange = TRUE WHERE transaction_id = @id", connection, transaction))
                {
                    command.Parameters.AddWithValue("@id", request.TransactionId);
                    await command.ExecuteNonQueryAsync();
                }

                var units = request.Items.Count;
                await TransactionStore.LogActivityAsync(connection, transaction, processedBy,
                    $"Exchanged {units} item{(units == 1 ? "" : "s")} on sale {referenceId}");

                await transaction.CommitAsync();
                return Ok(new
                {
                    exchangeId,
                    transactionId = request.TransactionId,
                    referenceId,
                    status = TransactionStatuses.Exchanged,
                    returned,
                    issued
                });
            }
            catch
            {
                await transaction.RollbackAsync();
                throw;
            }
        }

        private static async Task<IActionResult> Fail(NpgsqlTransaction transaction, IActionResult result)
        {
            await transaction.RollbackAsync();
            return result;
        }

        private static string WindowClosedMessage(DateTime saleUtc) =>
            $"The exchange period for this sale ended on {ShopClock.DisplayDate(ExchangePolicy.LastDay(saleUtc))}. " +
            $"Items can only be exchanged within {ExchangePolicy.WindowDays} days of purchase.";

        private static string Normalize(string value) => value.Trim().ToLowerInvariant();

        private static string Label(string size, string color) =>
            $"{(string.IsNullOrWhiteSpace(size) ? "One Size" : size)} / {(string.IsNullOrWhiteSpace(color) ? "Default" : color)}";

        private static string ExchangeReason(string oldSize, string oldColor, string newSize, string newColor)
        {
            var sizeChanged = Normalize(oldSize) != Normalize(newSize);
            var colorChanged = Normalize(oldColor) != Normalize(newColor);
            return sizeChanged && colorChanged ? "Change Size & Color" : sizeChanged ? "Change Size" : "Change Color";
        }
    }
}

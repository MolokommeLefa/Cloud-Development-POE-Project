using Microsoft.AspNetCore.Mvc;
using Azure.Data.Tables;
using CLDV6212_GROUP_04.Models;
using System.Threading.Tasks;
using System.Linq;
using Microsoft.AspNetCore.Mvc.Rendering;
using Azure;
using CLDV6212_GROUP_04.Service;
using Microsoft.Extensions.Caching.Memory;

namespace CLDV6212_GROUP_04.Controllers
{
    public class OrderController : Controller
    {
        private readonly IAzureStorageService _azureStorageService;
        private readonly TableServiceClient _tableServiceClient;
        private readonly TableClient _orderTableClient;
        private readonly TableClient _customerTableClient;
        private readonly TableClient _productTableClient;
        private readonly ILogger<OrderController> _logger;
        private readonly IMemoryCache _cache;
        private const string OrderTableName = "Orders";
        private const string CustomerTableName = "Customers";
        private const string ProductTableName = "Products";

        public OrderController(
            IAzureStorageService azureStorageService,
            TableServiceClient tableServiceClient,
            ILogger<OrderController> logger,
            IMemoryCache cache)
        {
            _azureStorageService = azureStorageService;
            _tableServiceClient = tableServiceClient;
            _logger = logger;
            _cache = cache;
            _orderTableClient = _tableServiceClient.GetTableClient(OrderTableName);
            _customerTableClient = _tableServiceClient.GetTableClient(CustomerTableName);
            _productTableClient = _tableServiceClient.GetTableClient(ProductTableName);
        }

        // GET: Order
        public async Task<IActionResult> Index()
        {
            try
            {
                await _tableServiceClient.CreateTableIfNotExistsAsync(OrderTableName);
                var orders = await _orderTableClient.QueryAsync<Order>()
                    .Where(o => o.PartitionKey == "Order")
                    .ToListAsync();

                return View(orders);
            }
            catch (Exception ex)
            {
                ViewBag.ErrorMessage = $"Error retrieving orders: {ex.Message}";
                return View(new List<Order>());
            }
        }

        // GET: Order/Create
        public async Task<IActionResult> Create()
        {
            await PopulateViewData();
            return View();
        }

        // POST: Order/Create
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create([Bind("CustomerId,ProductId,Quantity,OrderStatus")] Order order)
        {
            if (ModelState.IsValid)
            {
                Customer? customer = null;
                Product? product = null;
                Product? originalProduct = null;

                try
                {
                    await _tableServiceClient.CreateTableIfNotExistsAsync(OrderTableName);

                    // Get customer and product details with proper error handling and retry policies
                    try
                    {
                        var customerResponse = await _customerTableClient.GetEntityAsync<Customer>("Customer", order.CustomerId)
                            .WithRetryAsync();
                        customer = customerResponse.Value;
                    }
                    catch (RequestFailedException ex) when (ex.Status == 404)
                    {
                        ModelState.AddModelError("CustomerId", "Customer not found");
                        await PopulateViewData();
                        return View(order);
                    }

                    try
                    {
                        var productResponse = await _productTableClient.GetEntityAsync<Product>("Product", order.ProductId)
                            .WithRetryAsync();
                        product = productResponse.Value;
                        // Keep original product for rollback
                        originalProduct = new Product
                        {
                            PartitionKey = product.PartitionKey,
                            RowKey = product.RowKey,
                            ETag = product.ETag,
                            StockQuantity = product.StockQuantity,
                            Price = product.Price,
                            productName = product.productName,
                            productDescription = product.productDescription,
                            productImage = product.productImage,
                            Timestamp = product.Timestamp
                        };
                    }
                    catch (RequestFailedException ex) when (ex.Status == 404)
                    {
                        ModelState.AddModelError("ProductId", "Product not found");
                        await PopulateViewData();
                        return View(order);
                    }

                    // Validate stock
                    if (product.StockQuantity < order.Quantity)
                    {
                        ModelState.AddModelError("Quantity", $"Insufficient stock. Only {product.StockQuantity} available.");
                        await PopulateViewData();
                        return View(order);
                    }

                    // Set Azure Table properties
                    order.PartitionKey = "Order";
                    order.RowKey = Guid.NewGuid().ToString();
                    order.Timestamp = DateTimeOffset.UtcNow;
                    order.ETag = ETag.All;

                    // Set calculated fields
                    order.OrderDate = DateTime.UtcNow;
                    order.UnitPrice = (decimal)product.Price;
                    order.ProductName = product.productName;
                    order.Username = customer.Username;
                    order.CustomerName = $"{customer.FirstName} {customer.Surname}";

                    // Update product stock (this is the risky operation)
                    product.StockQuantity -= order.Quantity;
                    
                    try
                    {
                        // Update stock first with retry policy
                        await _productTableClient.UpdateEntityAsync(product, product.ETag)
                            .WithRetryAsync();
                        
                        // Invalidate products cache since stock changed
                        _cache.Remove("products_dropdown");
                        
                        // Then add order with retry policy
                        await _orderTableClient.AddEntityAsync(order)
                            .WithRetryAsync();

                        // Send notification message to queue for successful order
                        try
                        {
                            await _azureStorageService.SendMessageAsync("order-notifications", 
                                $"Order {order.OrderId} created for customer {order.CustomerName}");
                        }
                        catch (Exception ex)
                        {
                            _logger.LogWarning(ex, "Failed to send order notification for order {OrderId}", order.OrderId);
                            // Don't fail the whole transaction for notification issues
                        }

                        _logger.LogInformation("Order {OrderId} created successfully for customer {CustomerId}", 
                            order.OrderId, order.CustomerId);
                        
                        TempData["SuccessMessage"] = "Order created successfully!";
                        return RedirectToAction(nameof(Index));
                    }
                    catch (Exception orderException)
                    {
                        // Rollback stock update if order creation failed
                        try
                        {
                            if (originalProduct != null)
                            {
                                await _productTableClient.UpdateEntityAsync(originalProduct, ETag.All)
                                    .WithRetryAsync();
                                _logger.LogInformation("Successfully rolled back stock for product {ProductId}", order.ProductId);
                            }
                        }
                        catch (Exception rollbackException)
                        {
                            _logger.LogError(rollbackException, "Failed to rollback stock for product {ProductId} after order creation failure", order.ProductId);
                        }

                        _logger.LogError(orderException, "Failed to create order for customer {CustomerId}", order.CustomerId);
                        throw; // Re-throw the original exception
                    }
                }
                catch (Exception ex)
                {
                    // Detailed error logging
                    _logger.LogError(ex, "Error creating order for customer {CustomerId}: {Message}", order.CustomerId, ex.Message);
                    ModelState.AddModelError("", $"Error creating order: {ex.Message}");
                }
            }

            await PopulateViewData();
            return View(order);
        }

        // GET: Order/Edit/{id}
        public async Task<IActionResult> Edit(string id)
        {
            if (string.IsNullOrEmpty(id)) return NotFound();

            try
            {
                var order = await _orderTableClient.GetEntityAsync<Order>("Order", id);
                await PopulateViewData();
                return View(order.Value);
            }
            catch (Exception ex)
            {
                ViewBag.ErrorMessage = $"Error retrieving order: {ex.Message}";
                return View();
            }
        }

        // POST: Order/Edit/{id}
        // POST: Order/Edit/{id}
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(string id, [Bind("CustomerId,ProductId,Quantity,OrderStatus,PartitionKey,RowKey,Timestamp,ETag")] Order order)
        {
            if (id != order.RowKey) return NotFound();

            if (ModelState.IsValid)
            {
                try
                {
                    await _orderTableClient.UpdateEntityAsync(order, order.ETag);
                    TempData["SuccessMessage"] = "Order updated successfully!";
                    return RedirectToAction(nameof(Index));
                }
                catch (Exception ex)
                {
                    ModelState.AddModelError("", $"Error updating order: {ex.Message}");
                }
            }

            await PopulateViewData();
            return View(order);
        }

        // GET: Order/Details/{id}
        public async Task<IActionResult> Details(string id)
        {
            if (string.IsNullOrEmpty(id)) return NotFound();

            try
            {
                var order = await _orderTableClient.GetEntityAsync<Order>("Order", id);
                return View(order.Value);
            }
            catch (Exception ex)
            {
                ViewBag.ErrorMessage = $"Error retrieving order details: {ex.Message}";
                return View();
            }
        }

        // GET: Order/Delete/{id}
        public async Task<IActionResult> Delete(string id)
        {
            if (string.IsNullOrEmpty(id)) return NotFound();

            try
            {
                var order = await _orderTableClient.GetEntityAsync<Order>("Order", id);
                return View(order.Value);
            }
            catch (Exception ex)
            {
                ViewBag.ErrorMessage = $"Error retrieving order: {ex.Message}";
                return View();
            }
        }

        // POST: Order/Delete/{id}
        [HttpPost, ActionName("Delete")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteConfirmed(string id)
        {
            try
            {
                await _orderTableClient.DeleteEntityAsync("Order", id);
                TempData["SuccessMessage"] = "Order deleted successfully!";
            }
            catch (Exception ex)
            {
                TempData["ErrorMessage"] = $"Error deleting order: {ex.Message}";
            }

            return RedirectToAction(nameof(Index));
        }

        // Helper method to populate dropdowns with caching
        private async Task PopulateViewData()
        {
            const string customersCacheKey = "customers_dropdown";
            const string productsCacheKey = "products_dropdown";

            // Get customers with caching
            var customers = await _cache.GetOrCreateAsync<List<Customer>>(customersCacheKey, async entry =>
            {
                entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(10); // Cache for 10 minutes
                return await _customerTableClient.QueryAsync<Customer>()
                    .Where(c => c.PartitionKey == "Customer")
                    .ToListAsync();
            });

            // Get products with caching
            var products = await _cache.GetOrCreateAsync<List<Product>>(productsCacheKey, async entry =>
            {
                entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(5); // Cache for 5 minutes (products change more frequently)
                return await _productTableClient.QueryAsync<Product>()
                    .Where(p => p.PartitionKey == "Product" && p.StockQuantity > 0)
                    .ToListAsync();
            });

            ViewBag.Customers = new SelectList(customers, "RowKey", "Username");
            ViewBag.Products = new SelectList(products, "RowKey", "productName");
        }

        // AJAX method to get product details
        [HttpGet]
        public async Task<JsonResult> GetProductDetails(string productId)
        {
            try
            {
                var product = await _productTableClient.GetEntityAsync<Product>("Product", productId);
                return Json(new
                {
                    price = product.Value.Price,
                    stock = product.Value.StockQuantity,
                    name = product.Value.productName
                });
            }
            catch
            {
                return Json(null);
            }
        }
    }
}
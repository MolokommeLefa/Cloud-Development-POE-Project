using Microsoft.AspNetCore.Mvc;
using Azure.Data.Tables;
using CLDV6212_GROUP_04.Models;
using System.Threading.Tasks;
using System.Linq;
using Microsoft.AspNetCore.Mvc.Rendering;
using Azure;

namespace CLDV6212_GROUP_04.Controllers
{
    public class OrderController : Controller
    {
        private readonly TableServiceClient _tableServiceClient;
        private readonly TableClient _orderTableClient;
        private readonly TableClient _customerTableClient;
        private readonly TableClient _productTableClient;
        private const string OrderTableName = "Orders";
        private const string CustomerTableName = "Customers";
        private const string ProductTableName = "Products";

        public OrderController(IConfiguration configuration)
        {
            var connectionString = configuration.GetConnectionString("AzureStorageConnection");
            _tableServiceClient = new TableServiceClient(connectionString);
            _orderTableClient = new TableClient(connectionString, OrderTableName);
            _customerTableClient = new TableClient(connectionString, CustomerTableName);
            _productTableClient = new TableClient(connectionString, ProductTableName);
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
        // POST: Order/Create
        // POST: Order/Create
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create([Bind("CustomerId,ProductId,Quantity,OrderStatus")] Order order)
        {
            if (ModelState.IsValid)
            {
                try
                {
                    await _tableServiceClient.CreateTableIfNotExistsAsync(OrderTableName);

                    // Get customer and product details with proper error handling
                    Customer customer;
                    Product product;

                    try
                    {
                        var customerResponse = await _customerTableClient.GetEntityAsync<Customer>("Customer", order.CustomerId);
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
                        var productResponse = await _productTableClient.GetEntityAsync<Product>("Product", order.ProductId);
                        product = productResponse.Value;
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

                    // Update product stock
                    product.StockQuantity -= order.Quantity;
                    await _productTableClient.UpdateEntityAsync(product, product.ETag);

                    // Add order
                    await _orderTableClient.AddEntityAsync(order);

                    TempData["SuccessMessage"] = "Order created successfully!";
                    return RedirectToAction(nameof(Index));
                }
                catch (Exception ex)
                {
                    // Detailed error logging
                    Console.WriteLine($"ERROR: {ex.Message}");
                    Console.WriteLine($"StackTrace: {ex.StackTrace}");

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

        // Helper method to populate dropdowns
        private async Task PopulateViewData()
        {
            var customers = await _customerTableClient.QueryAsync<Customer>()
                .Where(c => c.PartitionKey == "Customer")
                .ToListAsync();

            var products = await _productTableClient.QueryAsync<Product>()
                .Where(p => p.PartitionKey == "Product" && p.StockQuantity > 0)
                .ToListAsync();

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
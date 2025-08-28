using Microsoft.AspNetCore.Mvc;
using Azure.Data.Tables;
using CLDV6212_GROUP_04.Models;
using System.Threading.Tasks;
using System.Linq;
using Microsoft.AspNetCore.Http;
using Azure.Storage.Blobs;
using System.IO;
using Azure;

namespace CLDV6212_GROUP_04.Controllers
{
    public class ProductController : Controller
    {
        private readonly TableServiceClient _tableServiceClient;
        private readonly TableClient _tableClient;
        private readonly BlobServiceClient _blobServiceClient;
        private const string TableName = "Products"; // Fixed table name
        private const string ContainerName = "product-images";

        public ProductController(IConfiguration configuration)
        {
            var connectionString = configuration.GetConnectionString("AzureStorageConnection");
            _tableServiceClient = new TableServiceClient(connectionString);
            _tableClient = new TableClient(connectionString, TableName);
            _blobServiceClient = new BlobServiceClient(connectionString);
        }

        // GET: Product
        public async Task<IActionResult> Index()
        {
            try
            {
                await _tableServiceClient.CreateTableIfNotExistsAsync(TableName);
                var products = await _tableClient.QueryAsync<Product>()
                    .Where(p => p.PartitionKey == "Product")
                    .ToListAsync();
                return View(products);
            }
            catch (Exception ex)
            {
                ViewBag.ErrorMessage = $"Error retrieving products: {ex.Message}";
                return View(new List<Product>());
            }
        }

        // GET: Product/Create
        public IActionResult Create()
        {
            return View();
        }

        // POST: Product/Create
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create([Bind("productName,productDescription,Price,StockQuantity,ImageUrl")] Product product, IFormFile productImage)
        {
            if (ModelState.IsValid)
            {
                try
                {
                    // Ensure table and container exist
                    await _tableServiceClient.CreateTableIfNotExistsAsync(TableName);
                    await _blobServiceClient.GetBlobContainerClient(ContainerName).CreateIfNotExistsAsync();

                    // Set Azure Table properties
                    product.PartitionKey = "Product";
                    product.RowKey = Guid.NewGuid().ToString();
                    product.Timestamp = DateTimeOffset.UtcNow;
                    product.ETag = ETag.All;

                    // Handle image upload if provided
                    if (productImage != null && productImage.Length > 0)
                    {
                        var imageUrl = await UploadProductImage(productImage, product.RowKey);
                        product.productImage = imageUrl; // FIXED: Store the URL
                    }

                    await _tableClient.AddEntityAsync(product);
                    TempData["SuccessMessage"] = "Product created successfully!";
                    return RedirectToAction(nameof(Index));
                }
                catch (Exception ex)
                {
                    ModelState.AddModelError("", $"Error creating product: {ex.Message}");
                }
            }
            return View(product);
        }

        // GET: Product/Edit/{id}
        public async Task<IActionResult> Edit(string id)
        {
            if (string.IsNullOrEmpty(id)) return NotFound();

            try
            {
                var product = await _tableClient.GetEntityAsync<Product>("Product", id);
                return View(product.Value);
            }
            catch (RequestFailedException ex) when (ex.Status == 404)
            {
                return NotFound();
            }
            catch (Exception ex)
            {
                ViewBag.ErrorMessage = $"Error retrieving product: {ex.Message}";
                return View();
            }
        }

        // POST: Product/Edit/{id}
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(string id, [Bind("productName,productDescription,Price,StockQuantity,ImageUrl,PartitionKey,RowKey,Timestamp,ETag")] Product product, IFormFile productImage)
        {
            if (id != product.RowKey) return NotFound();

            if (ModelState.IsValid)
            {
                try
                {
                    // Handle image upload if provided
                    if (productImage != null && productImage.Length > 0)
                    {
                        var imageUrl = await UploadProductImage(productImage, product.RowKey);
                        product.productImage = imageUrl; // FIXED: Store the URL
                    }

                    await _tableClient.UpdateEntityAsync(product, product.ETag);
                    TempData["SuccessMessage"] = "Product updated successfully!";
                    return RedirectToAction(nameof(Index));
                }
                catch (Exception ex)
                {
                    ModelState.AddModelError("", $"Error updating product: {ex.Message}");
                }
            }
            return View(product);
        }

        // GET: Product/Delete/{id}
        public async Task<IActionResult> Delete(string id)
        {
            if (string.IsNullOrEmpty(id)) return NotFound();

            try
            {
                var product = await _tableClient.GetEntityAsync<Product>("Product", id);
                return View(product.Value);
            }
            catch (RequestFailedException ex) when (ex.Status == 404)
            {
                return NotFound();
            }
            catch (Exception ex)
            {
                ViewBag.ErrorMessage = $"Error retrieving product: {ex.Message}";
                return View();
            }
        }

        // POST: Product/Delete/{id}
        [HttpPost, ActionName("Delete")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteConfirmed(string id)
        {
            try
            {
                await _tableClient.DeleteEntityAsync("Product", id);
                TempData["SuccessMessage"] = "Product deleted successfully!";
            }
            catch (Exception ex)
            {
                TempData["ErrorMessage"] = $"Error deleting product: {ex.Message}";
            }
            return RedirectToAction(nameof(Index));
        }

        // GET: Product/Details/{id}
        public async Task<IActionResult> Details(string id)
        {
            if (string.IsNullOrEmpty(id)) return NotFound();

            try
            {
                var product = await _tableClient.GetEntityAsync<Product>("Product", id);
                return View(product.Value);
            }
            catch (RequestFailedException ex) when (ex.Status == 404)
            {
                return NotFound();
            }
            catch (Exception ex)
            {
                ViewBag.ErrorMessage = $"Error retrieving product details: {ex.Message}";
                return View();
            }
        }

        // Helper method for image upload
        private async Task<string> UploadProductImage(IFormFile file, string productId)
        {
            try
            {
                var containerClient = _blobServiceClient.GetBlobContainerClient(ContainerName);
                await containerClient.CreateIfNotExistsAsync();

                // Generate unique filename
                var fileExtension = Path.GetExtension(file.FileName);
                var fileName = $"{productId}{fileExtension}";
                var blobClient = containerClient.GetBlobClient(fileName);

                // Upload the file
                using (var stream = file.OpenReadStream())
                {
                    await blobClient.UploadAsync(stream, true);
                }

                return blobClient.Uri.ToString();
            }
            catch (Exception ex)
            {
                // Log error and return empty string
                Console.WriteLine($"Error uploading image: {ex.Message}");
                return string.Empty;
            }
        }
    }
}
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using System.ComponentModel.DataAnnotations;
using Azure.Data.Tables;
using Microsoft.AspNetCore.Hosting;
using System.IO;
using System.Threading.Tasks;
using CLDV6212_GROUP_04.Models;
using CLDV6212_GROUP_04.Service;
using System.Collections.Generic;
using System;
using Azure.Storage.Files.Shares;
using Azure.Storage.Files.Shares.Models;
using Azure;
using Microsoft.Extensions.Caching.Memory;

namespace CLDV6212_GROUP_04.Controllers
{
    public class FileUploadController : Controller
    {
        private readonly IWebHostEnvironment _hostingEnvironment;
        private readonly IAzureStorageService _azureStorageService;
        private readonly TableServiceClient _tableServiceClient;
        private readonly ShareClient _fileShareClient;
        private readonly ILogger<FileUploadController> _logger;
        private readonly IMemoryCache _cache;

        public FileUploadController(IWebHostEnvironment hostingEnvironment,
                                  IAzureStorageService azureStorageService,
                                  TableServiceClient tableServiceClient,
                                  ShareClient fileShareClient,
                                  ILogger<FileUploadController> logger,
                                  IMemoryCache cache)
        {
            _hostingEnvironment = hostingEnvironment;
            _azureStorageService = azureStorageService;
            _tableServiceClient = tableServiceClient;
            _fileShareClient = fileShareClient;
            _logger = logger;
            _cache = cache;
        }

        // GET: FileUpload
        public async Task<IActionResult> Index()
        {
            var model = new FileUpload
            {
                OrderID = "ORD-" + DateTime.Now.ToString("yyyyMMdd-HHmmss"),
                CustomerName = "slya_05"
            };

            ViewBag.OrderList = await GetOrderListAsync();
            ViewBag.CustomerList = await GetCustomerListAsync();

            return View(model);
        }

        // POST: FileUpload
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Index(FileUpload model)
        {
            if (ModelState.IsValid)
            {
                try
                {
                    if (model.ProofOfPayment != null && model.ProofOfPayment.Length > 0)
                    {
                        // Validate file type
                        var allowedExtensions = new[] { ".pdf", ".jpg", ".jpeg", ".png", ".doc", ".docx" };
                        var fileExtension = Path.GetExtension(model.ProofOfPayment.FileName).ToLower();

                        if (!allowedExtensions.Contains(fileExtension))
                        {
                            ModelState.AddModelError("ProofOfPayment", "Only PDF, JPG, PNG, and DOC files are allowed.");
                            ViewBag.OrderList = await GetOrderListAsync();
                            ViewBag.CustomerList = await GetCustomerListAsync();
                            return View(model);
                        }

                        // Validate file size (max 5MB)
                        if (model.ProofOfPayment.Length > 5 * 1024 * 1024)
                        {
                            ModelState.AddModelError("ProofOfPayment", "File size cannot exceed 5MB.");
                            ViewBag.OrderList = await GetOrderListAsync();
                            ViewBag.CustomerList = await GetCustomerListAsync();
                            return View(model);
                        }

                        // Upload to Azure File Services
                        var (fileUrl, uniqueFileName) = await UploadToAzureFileStorage(model.ProofOfPayment);

                        // Save metadata to Azure Table Storage
                        await SaveToTableStorage(model, uniqueFileName, fileUrl);

                        TempData["SuccessMessage"] = "File uploaded successfully to Azure File Services!";
                        return RedirectToAction(nameof(Success));
                    }
                    else
                    {
                        ModelState.AddModelError("ProofOfPayment", "Please select a file to upload.");
                    }
                }
                catch (Exception ex)
                {
                    ModelState.AddModelError("", $"An error occurred while uploading the file: {ex.Message}");
                }
            }

            ViewBag.OrderList = await GetOrderListAsync();
            ViewBag.CustomerList = await GetCustomerListAsync();
            return View(model);
        }

        private async Task<(string fileUrl, string uniqueFileName)> UploadToAzureFileStorage(IFormFile file)
        {
            try
            {
                // Create the share if it doesn't exist with retry
                await _fileShareClient.CreateIfNotExistsAsync()
                    .WithRetryAsync();

                // Create directory for proof of payment files
                var directoryClient = _fileShareClient.GetDirectoryClient("proof-of-payment");
                await directoryClient.CreateIfNotExistsAsync()
                    .WithRetryAsync();

                // Generate unique filename
                var uniqueFileName = $"{Guid.NewGuid()}_{file.FileName}";
                var fileClient = directoryClient.GetFileClient(uniqueFileName);

                // Upload the file with retry
                using (var stream = file.OpenReadStream())
                {
                    await fileClient.CreateAsync(stream.Length)
                        .WithRetryAsync();
                    await fileClient.UploadRangeAsync(new HttpRange(0, stream.Length), stream)
                        .WithRetryAsync();
                }

                // Get the file URL (you might want to use a SAS token for secure access)
                var fileUrl = fileClient.Uri.ToString();

                return (fileUrl, uniqueFileName);
            }
            catch (RequestFailedException ex)
            {
                _logger.LogError(ex, "Azure File Storage error during upload");
                throw new Exception($"Azure File Storage error: {ex.Message}", ex);
            }
        }

        private async Task SaveToTableStorage(FileUpload model, string fileName, string fileUrl)
        {
            try
            {
                if (model.ProofOfPayment == null)
                    throw new ArgumentException("Proof of payment file is required");

                var tableClient = _tableServiceClient.GetTableClient("FileUploads");
                await tableClient.CreateIfNotExistsAsync()
                    .WithRetryAsync();

                var entity = new TableEntity("Upload", Guid.NewGuid().ToString())
                {
                    ["OrderID"] = model.OrderID,
                    ["CustomerName"] = model.CustomerName,
                    ["FileName"] = fileName,
                    ["OriginalFileName"] = model.ProofOfPayment.FileName,
                    ["FileSize"] = model.ProofOfPayment.Length,
                    ["ContentType"] = model.ProofOfPayment.ContentType,
                    ["UploadDate"] = DateTime.UtcNow,
                    ["FileUrl"] = fileUrl,
                    ["StorageType"] = "AzureFiles"
                };

                await tableClient.AddEntityAsync(entity)
                    .WithRetryAsync();
            }
            catch (Exception ex)
            {
                // Log the error
                Console.WriteLine($"Error saving to Table Storage: {ex.Message}");
                throw;
            }
        }

        // GET: FileUpload/Success
        public IActionResult Success()
        {
            if (TempData["SuccessMessage"] == null)
            {
                return RedirectToAction(nameof(Index));
            }

            return View();
        }

        // GET: FileUpload/Uploads
        public async Task<IActionResult> Uploads()
        {
            try
            {
                var uploads = await GetUploadsFromTableStorage();
                return View(uploads);
            }
            catch (Exception ex)
            {
                TempData["ErrorMessage"] = $"Error retrieving uploads: {ex.Message}";
                return View(new List<UploadViewModel>());
            }
        }

        private async Task<List<UploadViewModel>> GetUploadsFromTableStorage()
        {
            var uploads = new List<UploadViewModel>();

            try
            {
                var tableClient = _tableServiceClient.GetTableClient("FileUploads");

                var queryResults = tableClient.QueryAsync<TableEntity>();
                await foreach (var entity in queryResults)
                {
                    uploads.Add(new UploadViewModel
                    {
                        OrderID = entity.GetString("OrderID"),
                        CustomerName = entity.GetString("CustomerName"),
                        FileName = entity.GetString("FileName"),
                        OriginalFileName = entity.GetString("OriginalFileName"),
                        FileSize = entity.GetInt64("FileSize") ?? 0,
                        UploadDate = entity.GetDateTime("UploadDate") ?? DateTime.MinValue,
                        FileUrl = entity.GetString("FileUrl")
                    });
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error retrieving from Table Storage: {ex.Message}");
            }

            return uploads;
        }

        // GET: FileUpload/Download/{id}
        public async Task<IActionResult> Download(string id)
        {
            try
            {
                var tableClient = _tableServiceClient.GetTableClient("FileUploads");
                var entity = await tableClient.GetEntityAsync<TableEntity>("Upload", id);

                if (entity.Value == null)
                {
                    return NotFound();
                }

                var fileUrl = entity.Value.GetString("FileUrl");
                var originalFileName = entity.Value.GetString("OriginalFileName");
                var contentType = entity.Value.GetString("ContentType");

                // For Azure Files, you might want to generate a SAS token for secure download
                // This is a simplified version - in production, generate SAS tokens
                return Redirect(fileUrl);

                // Alternative: Download via stream (commented out)
                /*
                var shareClient = new ShareClient(connectionString, shareName);
                var fileClient = shareClient.GetRootDirectoryClient().GetFileClient(fileName);
                
                var response = await fileClient.DownloadAsync();
                return File(response.Value.Content, contentType, originalFileName);
                */
            }
            catch (Exception ex)
            {
                TempData["ErrorMessage"] = $"Error downloading file: {ex.Message}";
                return RedirectToAction(nameof(Uploads));
            }
        }

        // GET: FileUpload/Delete/{id}
        public async Task<IActionResult> Delete(string id)
        {
            try
            {
                var tableClient = _tableServiceClient.GetTableClient("FileUploads");
                var entity = await tableClient.GetEntityAsync<TableEntity>("Upload", id);

                if (entity.Value == null)
                {
                    return NotFound();
                }

                // Delete from Azure Files
                var fileName = entity.Value.GetString("FileName");
                await DeleteFromAzureFileStorage(fileName);

                // Delete from Table Storage
                await tableClient.DeleteEntityAsync("Upload", id);

                TempData["SuccessMessage"] = "File deleted successfully!";
            }
            catch (Exception ex)
            {
                TempData["ErrorMessage"] = $"Error deleting file: {ex.Message}";
            }

            return RedirectToAction(nameof(Uploads));
        }

        private async Task DeleteFromAzureFileStorage(string fileName)
        {
            try
            {
                var directoryClient = _fileShareClient.GetDirectoryClient("proof-of-payment");
                var fileClient = directoryClient.GetFileClient(fileName);

                if (await fileClient.ExistsAsync())
                {
                    await fileClient.DeleteAsync();
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error deleting from Azure Files: {ex.Message}");
                throw;
            }
        }

        private async Task<List<SelectListItem>> GetOrderListAsync()
        {
            const string ordersCacheKey = "orders_for_upload";
            
            var result = await _cache.GetOrCreateAsync<List<SelectListItem>>(ordersCacheKey, async entry =>
            {
                entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(5); // Cache for 5 minutes
                
                try
                {
                    var orderTableClient = _tableServiceClient.GetTableClient("Orders");
                    var orders = new List<SelectListItem>();

                    await foreach (var order in orderTableClient.QueryAsync<TableEntity>(filter: "PartitionKey eq 'Order'"))
                    {
                        var orderId = order.GetString("RowKey");
                        var customerName = order.GetString("CustomerName") ?? "Unknown Customer";
                        orders.Add(new SelectListItem 
                        { 
                            Value = orderId, 
                            Text = $"{orderId} - {customerName}"
                        });
                    }

                    // If no orders found, provide a fallback
                    if (!orders.Any())
                    {
                        orders.Add(new SelectListItem { Value = "No orders available", Text = "No orders available" });
                    }

                    return orders;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error retrieving orders for dropdown");
                    return new List<SelectListItem>
                    {
                        new SelectListItem { Value = "Error loading orders", Text = "Error loading orders" }
                    };
                }
            });

            return result ?? new List<SelectListItem>();
        }

        private async Task<List<SelectListItem>> GetCustomerListAsync()
        {
            const string customersCacheKey = "customers_for_upload";
            
            var result = await _cache.GetOrCreateAsync<List<SelectListItem>>(customersCacheKey, async entry =>
            {
                entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(10); // Cache for 10 minutes
                
                try
                {
                    var customerTableClient = _tableServiceClient.GetTableClient("Customers");
                    var customers = new List<SelectListItem>();

                    await foreach (var customer in customerTableClient.QueryAsync<TableEntity>(filter: "PartitionKey eq 'Customer'"))
                    {
                        var customerId = customer.GetString("RowKey");
                        var username = customer.GetString("Username") ?? "Unknown User";
                        var firstName = customer.GetString("FirstName") ?? "";
                        var surname = customer.GetString("Surname") ?? "";
                        var displayName = !string.IsNullOrEmpty(firstName) && !string.IsNullOrEmpty(surname) 
                            ? $"{firstName} {surname} ({username})" 
                            : username;
                        
                        customers.Add(new SelectListItem 
                        { 
                            Value = username, 
                            Text = displayName
                        });
                    }

                    // If no customers found, provide a fallback
                    if (!customers.Any())
                    {
                        customers.Add(new SelectListItem { Value = "slya_05", Text = "slya_05 (Default)" });
                    }

                    return customers;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error retrieving customers for dropdown");
                    return new List<SelectListItem>
                    {
                        new SelectListItem { Value = "slya_05", Text = "slya_05 (Default)" }
                    };
                }
            });

            return result ?? new List<SelectListItem>();
        }
    }

    // ViewModel for displaying uploads
    public class UploadViewModel
    {
        public string OrderID { get; set; } = string.Empty;
        public string CustomerName { get; set; } = string.Empty;
        public string FileName { get; set; } = string.Empty;
        public string OriginalFileName { get; set; } = string.Empty;
        public long FileSize { get; set; }
        public DateTime UploadDate { get; set; }
        public string FileUrl { get; set; } = string.Empty;
        public string PartitionKey { get; set; } = string.Empty;
        public string RowKey { get; set; } = string.Empty;
    }
}
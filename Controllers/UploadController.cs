using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using System.ComponentModel.DataAnnotations;
using Azure.Data.Tables;
using Microsoft.AspNetCore.Hosting;
using System.IO;
using System.Threading.Tasks;
using CLDV6212_GROUP_04.Models;
using System.Collections.Generic;
using System;
using Azure.Storage.Files.Shares;
using Azure.Storage.Files.Shares.Models;
using Azure;

namespace CLDV6212_GROUP_04.Controllers
{
    public class FileUploadController : Controller
    {
        private readonly IWebHostEnvironment _hostingEnvironment;
        private readonly TableServiceClient _tableServiceClient;
        private readonly ShareClient _fileShareClient;

        public FileUploadController(IWebHostEnvironment hostingEnvironment,
                                  TableServiceClient tableServiceClient,
                                  ShareClient fileShareClient)
        {
            _hostingEnvironment = hostingEnvironment;
            _tableServiceClient = tableServiceClient;
            _fileShareClient = fileShareClient;
        }

        // GET: FileUpload
        public IActionResult Index()
        {
            var model = new FileUpload
            {
                OrderID = "ORD-" + DateTime.Now.ToString("yyyyMMdd-HHmmss"),
                CustomerName = "slya_05"
            };

            ViewBag.OrderList = GetOrderList();
            ViewBag.CustomerList = GetCustomerList();

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
                            ViewBag.OrderList = GetOrderList();
                            ViewBag.CustomerList = GetCustomerList();
                            return View(model);
                        }

                        // Validate file size (max 5MB)
                        if (model.ProofOfPayment.Length > 5 * 1024 * 1024)
                        {
                            ModelState.AddModelError("ProofOfPayment", "File size cannot exceed 5MB.");
                            ViewBag.OrderList = GetOrderList();
                            ViewBag.CustomerList = GetCustomerList();
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

            ViewBag.OrderList = GetOrderList();
            ViewBag.CustomerList = GetCustomerList();
            return View(model);
        }

        private async Task<(string fileUrl, string uniqueFileName)> UploadToAzureFileStorage(IFormFile file)
        {
            try
            {
                // Create the share if it doesn't exist
                await _fileShareClient.CreateIfNotExistsAsync();

                // Create directory for proof of payment files
                var directoryClient = _fileShareClient.GetDirectoryClient("proof-of-payment");
                await directoryClient.CreateIfNotExistsAsync();

                // Generate unique filename
                var uniqueFileName = $"{Guid.NewGuid()}_{file.FileName}";
                var fileClient = directoryClient.GetFileClient(uniqueFileName);

                // Upload the file
                using (var stream = file.OpenReadStream())
                {
                    await fileClient.CreateAsync(stream.Length);
                    await fileClient.UploadRangeAsync(new HttpRange(0, stream.Length), stream);
                }

                // Get the file URL (you might want to use a SAS token for secure access)
                var fileUrl = fileClient.Uri.ToString();

                return (fileUrl, uniqueFileName);
            }
            catch (RequestFailedException ex)
            {
                throw new Exception($"Azure File Storage error: {ex.Message}", ex);
            }
        }

        private async Task SaveToTableStorage(FileUpload model, string fileName, string fileUrl)
        {
            try
            {
                var tableClient = _tableServiceClient.GetTableClient("FileUploads");
                await tableClient.CreateIfNotExistsAsync();

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

                await tableClient.AddEntityAsync(entity);
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

        private List<SelectListItem> GetOrderList()
        {
            return new List<SelectListItem>
            {
                new SelectListItem { Value = "ORD-20240101-0001", Text = "ORD-20240101-0001" },
                new SelectListItem { Value = "ORD-20240102-0002", Text = "ORD-20240102-0002" },
                new SelectListItem { Value = "ORD-20240103-0003", Text = "ORD-20240103-0003" },
                new SelectListItem { Value = "ORD-20240104-0004", Text = "ORD-20240104-0004" }
            };
        }

        private List<SelectListItem> GetCustomerList()
        {
            return new List<SelectListItem>
            {
                new SelectListItem { Value = "slya_05", Text = "slya_05" },
                new SelectListItem { Value = "john_doe", Text = "John Doe" },
                new SelectListItem { Value = "jane_smith", Text = "Jane Smith" },
                new SelectListItem { Value = "bob_wilson", Text = "Bob Wilson" }
            };
        }
    }

    // ViewModel for displaying uploads
    public class UploadViewModel
    {
        public string OrderID { get; set; }
        public string CustomerName { get; set; }
        public string FileName { get; set; }
        public string OriginalFileName { get; set; }
        public long FileSize { get; set; }
        public DateTime UploadDate { get; set; }
        public string FileUrl { get; set; }
        public string PartitionKey { get; set; }
        public string RowKey { get; set; }
    }
}
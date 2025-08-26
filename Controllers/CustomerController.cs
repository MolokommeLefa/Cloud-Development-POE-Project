using Azure;
using Azure.Data.Tables;
using CLDV6212_GROUP_04.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace CLDV6212_GROUP_04.Controllers
{
    public class CustomerController : Controller
    {
        private readonly TableServiceClient _tableServiceClient;
        private readonly TableClient _tableClient;
        private const string TableName = "Customers";
        private const string PartitionKeyValue = "Customer";

        public CustomerController(IConfiguration configuration)
        {
            var connectionString = configuration.GetConnectionString("AzureStorageConnection");
            _tableServiceClient = new TableServiceClient(connectionString);
            _tableClient = new TableClient(connectionString, TableName);
        }

        public async Task<IActionResult> Index()
        {
            try
            {
                await _tableServiceClient.CreateTableIfNotExistsAsync(TableName);
                var customers = await _tableClient.QueryAsync<Customer>()
                    .Where(c => c.PartitionKey == PartitionKeyValue)
                    .ToListAsync();
                return View(customers);
            }
            catch (Exception ex)
            {
                ViewBag.ErrorMessage = $"Error retrieving customers: {ex.Message}";
                return View(new List<Customer>());
            }
        }

        public IActionResult Create()
        {
            return View();
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create([Bind("FirstName,Surname,Email,Address,Username")] Customer customer)
        {
            if (ModelState.IsValid)
            {
                try
                {
                    // Check for duplicate username or email
                    var existingCustomer = await _tableClient.QueryAsync<Customer>()
                        .Where(c => c.Username == customer.Username || c.Email == customer.Email)
                        .FirstOrDefaultAsync();

                    if (existingCustomer != null)
                    {
                        ModelState.AddModelError("", "Username or email already exists");
                        return View(customer);
                    }

                    await _tableServiceClient.CreateTableIfNotExistsAsync(TableName);

                    // Initialize Azure Table properties
                    customer.PartitionKey = PartitionKeyValue;
                    customer.RowKey = Guid.NewGuid().ToString();
                    customer.Timestamp = DateTimeOffset.UtcNow;
                    customer.ETag = ETag.All;

                    await _tableClient.AddEntityAsync(customer);
                    TempData["SuccessMessage"] = "Customer created successfully!";
                    return RedirectToAction(nameof(Index));
                }
                catch (Exception ex)
                {
                    ModelState.AddModelError("", $"Error creating customer: {ex.Message}");
                }
            }
            return View(customer);
        }

        public async Task<IActionResult> Edit(string id)
        {
            if (string.IsNullOrEmpty(id)) return NotFound();

            try
            {
                var customer = await _tableClient.GetEntityAsync<Customer>(PartitionKeyValue, id);
                return View(customer.Value);
            }
            catch (RequestFailedException ex) when (ex.Status == 404)
            {
                return NotFound();
            }
            catch (Exception ex)
            {
                ViewBag.ErrorMessage = $"Error retrieving customer: {ex.Message}";
                return View();
            }
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(string id, [Bind("FirstName,Surname,Email,Address,Username,PartitionKey,RowKey,ETag")] Customer customer)
        {
            if (id != customer.RowKey) return NotFound();

            if (ModelState.IsValid)
            {
                try
                {
                    // Update timestamp and maintain partition key
                    customer.Timestamp = DateTimeOffset.UtcNow;
                    customer.PartitionKey = PartitionKeyValue;

                    await _tableClient.UpdateEntityAsync(customer, customer.ETag);
                    TempData["SuccessMessage"] = "Customer updated successfully!";
                    return RedirectToAction(nameof(Index));
                }
                catch (Exception ex)
                {
                    ModelState.AddModelError("", $"Error updating customer: {ex.Message}");
                }
            }
            return View(customer);
        }

        public async Task<IActionResult> Delete(string id)
        {
            if (string.IsNullOrEmpty(id)) return NotFound();

            try
            {
                var customer = await _tableClient.GetEntityAsync<Customer>(PartitionKeyValue, id);
                return View(customer.Value);
            }
            catch (RequestFailedException ex) when (ex.Status == 404)
            {
                return NotFound();
            }
            catch (Exception ex)
            {
                ViewBag.ErrorMessage = $"Error retrieving customer: {ex.Message}";
                return View();
            }
        }

        [HttpPost, ActionName("Delete")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteConfirmed(string id)
        {
            try
            {
                await _tableClient.DeleteEntityAsync(PartitionKeyValue, id);
                TempData["SuccessMessage"] = "Customer deleted successfully!";
            }
            catch (Exception ex)
            {
                TempData["ErrorMessage"] = $"Error deleting customer: {ex.Message}";
            }
            return RedirectToAction(nameof(Index));
        }

        public async Task<IActionResult> Details(string id)
        {
            if (string.IsNullOrEmpty(id)) return NotFound();

            try
            {
                var customer = await _tableClient.GetEntityAsync<Customer>(PartitionKeyValue, id);
                return View(customer.Value);
            }
            catch (RequestFailedException ex) when (ex.Status == 404)
            {
                return NotFound();
            }
            catch (Exception ex)
            {
                ViewBag.ErrorMessage = $"Error retrieving customer details: {ex.Message}";
                return View();
            }
        }
    }
}
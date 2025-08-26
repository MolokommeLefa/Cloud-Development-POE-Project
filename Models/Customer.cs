using Microsoft.AspNetCore.Mvc;
using Azure.Data.Tables;
using Azure;
using System.ComponentModel.DataAnnotations;

namespace CLDV6212_GROUP_04.Models
{
    public class Customer : ITableEntity
    {
        // Azure Table Properties with defaults
        public string PartitionKey { get; set; } = "Customer";
        public string RowKey { get; set; } = Guid.NewGuid().ToString();
        public DateTimeOffset? Timestamp { get; set; } 
        public ETag ETag { get; set; } = ETag.All;

        // Computed property
        [Display(Name = "Customer ID")]
        public string CustomerId => RowKey;

        // Customer properties with proper initialization
        [Required(ErrorMessage = "First name is required")]
        [Display(Name = "First Name")]
        public string FirstName { get; set; } = string.Empty;

        [Required(ErrorMessage = "Surname is required")]
        [Display(Name = "Surname")]
        public string Surname { get; set; } = string.Empty;

        [Required(ErrorMessage = "Email is required")]
        [EmailAddress(ErrorMessage = "Please enter a valid email address")]
        [Display(Name = "Email")]
        public string Email { get; set; } = string.Empty;

        [Required(ErrorMessage = "Address is required")]
        [Display(Name = "Shipping Address")]
        public string Address { get; set; } = string.Empty;

        [Required(ErrorMessage = "Username is required")]
        [Display(Name = "Username")]
        public string Username { get; set; } = string.Empty;

        // Default constructor required for Azure Table SDK
        public Customer() { }

        // Optional: Convenience constructor
        public Customer(string firstName, string surname, string email, string address, string username)
        {
            FirstName = firstName;
            Surname = surname;
            Email = email;
            Address = address;
            Username = username;
            PartitionKey = "Customer";
            RowKey = Guid.NewGuid().ToString();
        }
    }
}
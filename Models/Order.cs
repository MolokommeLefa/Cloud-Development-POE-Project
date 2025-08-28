using System.ComponentModel.DataAnnotations;
using Azure;
using Azure.Data.Tables;

namespace CLDV6212_GROUP_04.Models
{
    public class Order : ITableEntity
    {
        // Azure Table Properties with defaults
        public string PartitionKey { get; set; } = "Order";
        public string RowKey { get; set; } = Guid.NewGuid().ToString();
        public DateTimeOffset? Timestamp { get; set; }
        public ETag ETag { get; set; } = ETag.All;

        // Computed property
        [Display(Name = "Order ID")]
        public string OrderId => RowKey;

        [Required(ErrorMessage = "Customer is required")]
        [Display(Name = "Customer ID")]
        public string CustomerId { get; set; } = string.Empty;

        [Required(ErrorMessage = "Product is required")]
        [Display(Name = "Product ID")]
        public string ProductId { get; set; } = string.Empty;

        [Required(ErrorMessage = "Order date is required")]
        [Display(Name = "Order Date")]
        public DateTime OrderDate { get; set; } = DateTime.UtcNow;

        // REMOVED Range validation from calculated field
        [Display(Name = "Total Price")]
        public decimal TotalPrice => UnitPrice * Quantity;

        [Required(ErrorMessage = "Username is required")]
        [Display(Name = "Username")]
        public string Username { get; set; } = string.Empty;

        [Required(ErrorMessage = "Product name is required")]
        [Display(Name = "Product Name")]
        public string ProductName { get; set; } = string.Empty;

        [Required(ErrorMessage = "Quantity is required")]
        [Display(Name = "Quantity")]
        [Range(1, int.MaxValue, ErrorMessage = "Quantity must be at least 1")]
        public int Quantity { get; set; }

        [Required(ErrorMessage = "Order status is required")]
        [Display(Name = "Order Status")]
        public string OrderStatus { get; set; } = "Pending";

        public string CustomerName { get; set; } = string.Empty;
        public decimal UnitPrice { get; set; }

        public Order() { }
    }
}
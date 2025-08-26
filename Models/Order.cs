using System.ComponentModel.DataAnnotations;
using Azure;

namespace CLDV6212_GROUP_04.Models
{
    public class Order
    {


        public string PartitionKey { get; set; }
        public string RowKey { get; set; }
        public DateTimeOffset? Timestamp { get; set; }
        public ETag ETag { get; set; }


        [Display(Name = "Order ID")]
        public string OrderId => RowKey;

        [Required]
        [Display(Name = "Customer ID")]
        public string CustomerId { get; set; }

        [Required]
        [Display(Name = "Order Date")]
        public DateTime OrderDate { get; set; }

        [Required]
        [Display(Name = "Total")]
        public decimal Totalprice { get; set; }

        public string username
        { get; set; } = string.Empty;

        [Required]
        [Display(Name = "Order Status")]
        public string OrderStatus { get; set; }
    }
}

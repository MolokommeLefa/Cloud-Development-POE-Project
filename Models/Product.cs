using System.ComponentModel.DataAnnotations;
using Azure;
using Azure.Data.Tables;

namespace CLDV6212_GROUP_04.Models
{
    public class Product : ITableEntity
    {
        public string PartitionKey { get; set; } = "Product";
        public string RowKey { get; set; } = Guid.NewGuid().ToString();
       
        public int StockQuantity { get; set; }
        public DateTimeOffset? Timestamp { get; set; }
        public ETag ETag { get; set; } = ETag.All;

        [Display(Name = "Product ID")]
        public string ProductID => RowKey;

        [Required(ErrorMessage = "Product Name is required")]
        [Display(Name = "Product Name")]
        public string productName { get; set; } = string.Empty;

        [Required(ErrorMessage = "Product Description is required")]
        [Display(Description = "Product Description")]
        public string productDescription { get; set; } = string.Empty;


        [Required(ErrorMessage = "Product Price is required")]
        [Display(Name = "Product Price")]
        public double Price { get; set; }

        [Display(Name = "Image URL")]
        public string productImage { get; set; } = string.Empty;


        // Default constructor for TableEntity
        public Product()
        {

    }
    }
}

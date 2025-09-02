using System.ComponentModel.DataAnnotations;
using Azure.Data.Tables;

namespace CLDV6212_GROUP_04.Models
{
    public class FileUpload
    {
        [Required]
        [Display(Name = "Proof of Payment")]
        public IFormFile? ProofOfPayment { get; set; }

        [Required]
        [Display(Name = "Order ID")]
        public string OrderID { get; set; } = string.Empty;

        [Required]
        [Display(Name = "Customer Name")]
        public string CustomerName { get; set; } = string.Empty;
    }
}

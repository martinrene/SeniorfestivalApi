using Seniorfestival.Data.Models.Base;

namespace Seniorfestival.Data.Models
{
    [Table("Shops")]
    public class Shop : EntityBase
    {
        public string? Title { get; set; }
        public string? Description { get; set; }
        public string? PictureUrl { get; set; }
        public bool IsShop { get; set; }
        public string? Location { get; set; }
        public string? Links { get; set; }

        public int Id()
        {
            return Convert.ToInt32(this.RowKey);

        }
    }
}
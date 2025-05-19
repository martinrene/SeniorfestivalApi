using Seniorfestival.Data.Models.Base;

namespace Seniorfestival.Data.Models
{
    [Table("Texts")]
    public class Text : EntityBase
    {
        public string? Description { get; set; }
    }
}
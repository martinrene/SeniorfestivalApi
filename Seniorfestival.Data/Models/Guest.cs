using Seniorfestival.Data.Models.Base;

namespace Seniorfestival.Data.Models
{
    [Table("Guests")]
    public class Guest : EntityBase
    {
        public string Name { get; set; } = "";
        public string Group { get; set; } = "";
    }
}
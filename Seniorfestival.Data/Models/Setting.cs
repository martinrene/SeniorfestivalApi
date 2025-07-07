using Seniorfestival.Data.Models.Base;

namespace Seniorfestival.Data.Models
{
    [Table("Settings")]
    public class Setting : EntityBase
    {
        public string Name { get; set; } = "";
        public string Value { get; set; } = "";
    }
}
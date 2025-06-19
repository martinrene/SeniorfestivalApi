using Seniorfestival.Data.Models.Base;

namespace Seniorfestival.Data.Models
{
    [Table("Actives")]
    public class Setting : EntityBase
    {
        public bool Active { get; set; }
    }
}
using Seniorfestival.Data.Models.Base;

namespace Seniorfestival.Data.Models
{
    [Table("Events2")]
    public class Evento : EntityBase
    {
        public string? Start { get; set; }
        public string? End { get; set; }
        public string? Day { get; set; }
        public string? Title { get; set; }
        public string? Description { get; set; }
        public string? PictureUrl { get; set; }
        public string? Links { get; set; }
        public string? Location { get; set; }
        public bool Public { get; set; }

        public int Id()
        {
            return Convert.ToInt32(this.RowKey);

        }
    }
}
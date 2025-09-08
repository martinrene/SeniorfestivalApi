using Seniorfestival.Data.Models.Base;

namespace Seniorfestival.Data.Models
{
    [Table("Votings")]
    public class Voting : EntityBase
    {
        // Rowkey will be the id of the voting
        public string VotingId
        {
            get
            {
                return this.RowKey;
            }
            set
            {
                this.RowKey = value;
            }
        }
        public string Description { get; set; } = "";
        public string Choices { get; set; } = "";
        public bool Active { get; set; }
    }
}
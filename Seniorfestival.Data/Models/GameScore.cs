using Seniorfestival.Data.Models.Base;

namespace Seniorfestival.Data.Models
{
    [Table("GameScores")]
    public class GameScore : EntityBase
    {
        // PartitionKey is the festival-local day, as yyyy-MM-dd, so a single day's
        // leaderboard is one partition query.
        public string Day
        {
            get
            {
                return this.PartitionKey;
            }
            set
            {
                this.PartitionKey = value;
            }
        }

        // RowKey is the phone id, so each device keeps one row per day: its best.
        public string PhoneId
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

        public string Name { get; set; } = "";
        public int Score { get; set; }
    }
}

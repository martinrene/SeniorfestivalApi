using Seniorfestival.Data.Models.Base;

namespace Seniorfestival.Data.Models
{
    [Table("Votes")]
    public class Vote : EntityBase
    {
        // PartitionKey will be voting id
        public string VotingId
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

        // RowKey will be id of the phone
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

        public string Choice { get; set; } = "";
    }
}
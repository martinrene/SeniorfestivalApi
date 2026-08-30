using Seniorfestival.Data.Models.Base;

namespace Seniorfestival.Data.Models
{
    [Table("QueueNumbers")]
    public class QueueNumber : EntityBase
    {
        // PartitionKey is the id of the event/activity being queued for
        public string EventId
        {
            get => this.PartitionKey;
            set => this.PartitionKey = value;
        }

        // RowKey is the queue number handed to the guest
        public string Number
        {
            get => this.RowKey;
            set => this.RowKey = value;
        }

        public string PhoneId { get; set; } = "";
        public string Name { get; set; } = "";
        public bool Done { get; set; }
        public DateTimeOffset? DoneAt { get; set; }
    }
}

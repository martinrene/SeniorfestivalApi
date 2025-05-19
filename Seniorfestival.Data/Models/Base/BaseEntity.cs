using Azure;
using Azure.Data.Tables;

namespace Seniorfestival.Data.Models.Base
{
    public class EntityBase : ITableEntity
    {
        public string PartitionKey { get; set; } = "";
        public string RowKey { get; set; } = "";
        public DateTimeOffset? Timestamp { get; set; }
        public ETag ETag { get; set; }
    }
}

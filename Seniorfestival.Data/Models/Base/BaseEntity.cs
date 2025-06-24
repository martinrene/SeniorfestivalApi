using System.Text.Json.Serialization;
using Azure;
using Azure.Data.Tables;

namespace Seniorfestival.Data.Models.Base
{
    public class EntityBase : ITableEntity
    {
        [JsonIgnore]
        public string PartitionKey { get; set; } = "";
        public string RowKey { get; set; } = "";
        [JsonIgnore]
        public DateTimeOffset? Timestamp { get; set; }
        [JsonIgnore]
        public ETag ETag { get; set; }
    }
}

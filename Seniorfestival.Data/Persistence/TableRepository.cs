using Azure;
using Azure.Data.Tables;


namespace Seniorfestival.Data.Persistence
{
    public class TableRepository<T> : ITableRepository<T> where T : class, ITableEntity
    {
        readonly TableClient _tableClient;

        public TableRepository(TableServiceClient tableServiceClient)
        {
            var tableName = "";
            try
            {
                var tableAttribute = Attribute.GetCustomAttribute(typeof(T), typeof(TableAttribute));
                if (tableAttribute != null)
                {
                    tableName = ((TableAttribute)tableAttribute).name;
                }
                else
                {
                    tableName = typeof(T).Name;
                }
            }
            catch
            {
                tableName = typeof(T).Name;
            }

            _tableClient = tableServiceClient.GetTableClient(tableName);
        }

        public async Task AddAsync(T item)
        {
            //var entity = item.ValidateTableStorageEntity();

            await _tableClient.AddEntityAsync(item);
        }

        public async Task<T> GetAsync(string partitionKey, string rowKey)
        {
            return await _tableClient.GetEntityAsync<T>(partitionKey, rowKey);
        }

        public async Task<List<T>> GetFromQueryAsync(string filter)
        {
            AsyncPageable<T> queryResult = _tableClient.QueryAsync<T>(filter);

            List<T> result = new List<T>();

            await foreach (T item in queryResult)
            {
                result.Add(item);
            }

            return result;
        }

        /*
        public async Task<T> GetAsync(Guid partitionKey)
        {
            return await _tableClient.QueryAsync<T>()
        }
        */

        public async Task UpdateAsync(T item)
        {
            //var entity = item.ValidateTableStorageEntity();

            await _tableClient.UpdateEntityAsync(item, Azure.ETag.All, TableUpdateMode.Replace);
        }

        public async Task RemoveAsync(T item)
        {
            await _tableClient.DeleteEntityAsync(item.PartitionKey, item.RowKey);
        }
    }
}

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Seniorfestival.Data.Persistence
{
    public interface ITableRepository<T>
    {
        public Task AddAsync(T item);
        public Task UpdateAsync(T item);
        public Task RemoveAsync(T item);
        public Task<T> GetAsync(Guid partitionKey, Guid rowKey);
        public Task<List<T>> GetFromQueryAsync(string filter);
    }
}

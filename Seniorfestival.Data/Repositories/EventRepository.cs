using Seniorfestival.Data.Models;
using Seniorfestival.Data.Persistence;

namespace Seniorfestival.Data.Repositories
{
    public class EventRepository : IEventRepository
    {
        private readonly ITableRepository<Event> repository;

        public EventRepository(ITableRepository<Event> repository)
        {
            this.repository = repository;
        }

        public async Task<Event[]> ReadAllEvents()
        {
            return (await repository.GetFromQueryAsync("")).Where(e => e.Public).ToArray();
        }

        public async Task<Event[]> ReadEventsByPartition(string partitionKey)
        {
            return (await repository.GetFromQueryAsync($"PartitionKey eq '{partitionKey}'")).ToArray();
        }

        public async Task UpsertEvent(Event evt)
        {
            await repository.UpsertAsync(evt);
        }

        public async Task DeleteEvent(Event evt)
        {
            await repository.RemoveAsync(evt);
        }
    }
}
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
            return (await repository.GetFromQueryAsync("")).ToArray();
        }
    }
}
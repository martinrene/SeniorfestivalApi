using Seniorfestival.Data.Models;
using Seniorfestival.Data.Persistence;

namespace Seniorfestival.Data.Repositories
{
    public class EventRepository : IEventRepository
    {
        private readonly ITableRepository<Evento> repository;

        public EventRepository(ITableRepository<Evento> repository)
        {
            this.repository = repository;
        }

        public async Task<Evento[]> ReadAllEvents()
        {
            return (await repository.GetFromQueryAsync("")).ToArray();
        }
    }
}
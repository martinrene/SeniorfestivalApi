using Seniorfestival.Data.Models;
using Seniorfestival.Data.Persistence;

namespace Seniorfestival.Data.Repositories
{
    public class GuestRepository : IGuestRepository
    {
        private readonly ITableRepository<Guest> repository;

        public GuestRepository(ITableRepository<Guest> repository)
        {
            this.repository = repository;
        }

        public async Task<Guest[]> ReadAllGuests()
        {
            return (await repository.GetFromQueryAsync("")).ToArray();
        }
    }
}
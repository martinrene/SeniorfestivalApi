using Seniorfestival.Data.Models;
using Seniorfestival.Data.Persistence;

namespace Seniorfestival.Data.Repositories
{
    public class ShopRepository : IShopRepository
    {
        private readonly ITableRepository<Shop> repository;

        public ShopRepository(ITableRepository<Shop> repository)
        {
            this.repository = repository;
        }

        public async Task<Shop[]> ReadAllShops()
        {
            return (await repository.GetFromQueryAsync("")).ToArray();
        }
    }
}
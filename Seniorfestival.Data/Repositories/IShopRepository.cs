using Seniorfestival.Data.Models;

namespace Seniorfestival.Data.Repositories
{
    public interface IShopRepository
    {
        Task<Shop[]> ReadAllShops();
    }
}
using Seniorfestival.Data.Models;

namespace Seniorfestival.Data.Repositories
{
    public interface IGuestRepository
    {
        Task<Guest[]> ReadAllGuests();

        Task UpsertGuest(Guest guest);

        Task DeleteGuest(Guest guest);
    }
}

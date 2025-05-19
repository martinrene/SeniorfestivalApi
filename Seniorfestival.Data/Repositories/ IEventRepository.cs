using Seniorfestival.Data.Models;

namespace Seniorfestival.Data.Repositories
{
    public interface IEventRepository
    {
        Task<Evento[]> ReadAllEvents();
    }
}
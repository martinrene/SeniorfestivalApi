using Seniorfestival.Data.Models;

namespace Seniorfestival.Data.Repositories
{
    public interface IEventRepository
    {
        Task<Event[]> ReadAllEvents();
        Task<Event[]> ReadEventsByPartition(string partitionKey);
        Task UpsertEvent(Event evt);
        Task DeleteEvent(Event evt);
    }
}
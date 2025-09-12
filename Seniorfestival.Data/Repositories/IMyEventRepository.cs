using Seniorfestival.Data.Models;

namespace Seniorfestival.Data.Repositories
{
    public interface IMyEventRepository
    {
        Task<MyEvent[]> ReadAllMyEvents(string phoneId);
        Task AddToMyEvents(string phoneId, string eventId);

        Task RemoveFromMyEvents(string phoneId, string eventId);

        Task<MyEvent[]> MyEventsForEventId(string eventId);
    }
}
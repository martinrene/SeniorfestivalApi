using Seniorfestival.Data.Models;

namespace Seniorfestival.Data.Repositories
{
    public interface IEventRepository
    {
        Task<Event[]> ReadAllEvents();
        Task<Event[]> ReadEventsByPartition(string partitionKey);
        Task<Event?> FindById(string eventId);
        Task<Event?> FindByQrCode(string qrCode);
        Task RecordServiceCompletion(string eventId);
        Task UpsertEvent(Event evt);
        Task DeleteEvent(Event evt);
    }
}
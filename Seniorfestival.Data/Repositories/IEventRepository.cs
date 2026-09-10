using Seniorfestival.Data.Models;

namespace Seniorfestival.Data.Repositories
{
    public interface IEventRepository
    {
        Task<Event[]> ReadAllEvents();
        Task<Event[]> ReadEventsByPartition(string partitionKey);
        Task<Event?> FindById(string eventId);
        /// <summary>
        /// The row a guest scanning <paramref name="qrCode"/> on <paramref name="day"/>
        /// should be queued for. An activity running several days is one printed code and
        /// one row per day, each with its own queue.
        /// </summary>
        Task<Event?> FindByQrCode(string qrCode, string day);

        /// <summary>Every row carrying <paramref name="qrCode"/>, across all days.</summary>
        Task<Event[]> ReadEventsByQrCode(string qrCode);
        Task RecordServiceCompletion(string eventId);
        Task UpsertEvent(Event evt);
        Task DeleteEvent(Event evt);
    }
}
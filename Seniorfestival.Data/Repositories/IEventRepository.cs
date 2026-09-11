using Seniorfestival.Data.Models;

namespace Seniorfestival.Data.Repositories
{
    public interface IEventRepository
    {
        Task<Event[]> ReadAllEvents();
        Task<Event[]> ReadEventsByPartition(string partitionKey);
        Task<Event?> FindById(string eventId);
        /// <summary>
        /// The sessions a guest scanning <paramref name="qrCode"/> at <paramref name="nowFestival"/>
        /// should be queued for. One printed code covers every row of the same activity: the days
        /// it runs, and the times it runs on each of them. Empty when the code is unknown.
        /// </summary>
        Task<Event[]> FindSessionsByQrCode(string qrCode, DateTime nowFestival);

        /// <summary>
        /// The sessions sharing <paramref name="evt"/>'s code and day - the group whose queue
        /// its tickets belong to. Just the row itself when it carries no code.
        /// </summary>
        Task<Event[]> ReadSessionsForEvent(Event evt);

        /// <summary>Every row carrying <paramref name="qrCode"/>, across all days.</summary>
        Task<Event[]> ReadEventsByQrCode(string qrCode);
        Task RecordServiceCompletion(string eventId);
        Task UpsertEvent(Event evt);
        Task DeleteEvent(Event evt);
    }
}
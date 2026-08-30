using Seniorfestival.Data.Models;

namespace Seniorfestival.Data.Repositories
{
    public interface IQueueNumberRepository
    {
        Task<QueueNumber?> FindActiveQueueNumber(string eventId, string phoneId);
        Task<QueueNumber> AddToQueue(string eventId, string phoneId, string name);
        Task<QueueNumber?> MarkDone(string eventId, string number);
        Task<QueueNumber[]> ReadActiveQueueForEvent(string eventId);
        Task<QueueNumber[]> ReadActiveQueueForPhone(string phoneId);
    }
}

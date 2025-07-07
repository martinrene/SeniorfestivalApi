using Seniorfestival.Data.Models;
using Seniorfestival.Data.Persistence;

namespace Seniorfestival.Data.Repositories
{
    public class MyEventRepository : IMyEventRepository
    {
        private readonly ITableRepository<MyEvent> repository;

        public MyEventRepository(ITableRepository<MyEvent> repository)
        {
            this.repository = repository;
        }

        public async Task AddToMyEvents(string phoneId, string eventId)
        {
            MyEvent subscription = new MyEvent() { PartitionKey = phoneId, RowKey = eventId };
            try
            {
                await repository.AddAsync(subscription);
            }
            catch { } //Fails if subscription is already added but we do not care as long as it is in the table
        }

        public async Task<MyEvent[]> ReadAllMyEvents(string phoneId)
        {
            MyEvent[] events = (await repository.GetFromQueryAsync($"PartitionKey eq '{phoneId}'")).ToArray();
            return events;
        }

        public async Task<MyEvent[]> ReadMyEventsFromListOfEventIds(string eventId)
        {
            MyEvent[] events = (await repository.GetFromQueryAsync($"RowKey eq '{eventId}'")).ToArray();

            throw new NotImplementedException();
        }

        public async Task RemoveFromMyEvents(string phoneId, string eventId)
        {
            MyEvent subscriptionToRemove = await repository.GetAsync(phoneId, eventId);
        }
    }
}
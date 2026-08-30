using Seniorfestival.Data.Models;
using Seniorfestival.Data.Persistence;

namespace Seniorfestival.Data.Repositories
{
    public class EventRepository : IEventRepository
    {
        private readonly ITableRepository<Event> repository;

        public EventRepository(ITableRepository<Event> repository)
        {
            this.repository = repository;
        }

        public async Task<Event[]> ReadAllEvents()
        {
            return (await repository.GetFromQueryAsync("")).Where(e => e.Public).ToArray();
        }

        public async Task<Event[]> ReadEventsByPartition(string partitionKey)
        {
            return (await repository.GetFromQueryAsync($"PartitionKey eq '{partitionKey}'")).ToArray();
        }

        public async Task<Event?> FindById(string eventId)
        {
            var matches = await repository.GetFromQueryAsync($"RowKey eq '{eventId}'");
            return matches.FirstOrDefault();
        }

        public async Task<Event?> FindByQrCode(string qrCode)
        {
            var matches = await repository.GetFromQueryAsync($"QrCode eq '{qrCode}'");
            return matches.FirstOrDefault();
        }

        public async Task RecordServiceCompletion(string eventId)
        {
            Event? evt = await FindById(eventId);
            if (evt == null)
            {
                return;
            }

            var now = DateTimeOffset.UtcNow;

            if (evt.LastServedAt.HasValue)
            {
                int gapMinutes = Math.Max(0, (int)Math.Round((now - evt.LastServedAt.Value).TotalMinutes));
                evt.RecentServiceMinutes = PrependServiceMinutes(evt.RecentServiceMinutes, gapMinutes);
            }

            evt.LastServedAt = now;

            await repository.UpsertAsync(evt);
        }

        private static string PrependServiceMinutes(string? existing, int minutes)
        {
            var values = (existing ?? "")
                .Split(',', StringSplitOptions.RemoveEmptyEntries)
                .ToList();

            values.Insert(0, minutes.ToString());

            return string.Join(",", values.Take(3));
        }

        public async Task UpsertEvent(Event evt)
        {
            await repository.UpsertAsync(evt);
        }

        public async Task DeleteEvent(Event evt)
        {
            await repository.RemoveAsync(evt);
        }
    }
}
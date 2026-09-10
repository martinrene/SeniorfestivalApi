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

        public async Task<Event[]> ReadEventsByQrCode(string qrCode)
        {
            return (await repository.GetFromQueryAsync($"QrCode eq '{qrCode}'")).ToArray();
        }

        public async Task<Event?> FindByQrCode(string qrCode, string day)
        {
            var matches = await ReadEventsByQrCode(qrCode);

            // The common case: the code belongs to a single-day activity, and the day the
            // guest is standing there on does not come into it.
            if (matches.Length <= 1)
            {
                return matches.FirstOrDefault();
            }

            string today = FestivalDay.Normalize(day);
            var todaysEvent = matches.FirstOrDefault(e => FestivalDay.Normalize(e.Day) == today);

            if (todaysEvent != null)
            {
                return todaysEvent;
            }

            // Nothing runs today - the festival has not started, or this activity is not on
            // today. Hand back the next day it does run rather than nothing at all, so
            // scanning a sign still works while the festival is being set up.
            var byDay = matches
                .OrderBy(e => FestivalDay.Rank(e.Day))
                .ThenBy(e => e.RowKey)
                .ToArray();

            return byDay.FirstOrDefault(e => FestivalDay.Rank(e.Day) >= FestivalDay.Rank(day))
                ?? byDay[0];
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
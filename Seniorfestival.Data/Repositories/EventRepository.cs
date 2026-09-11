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

        public async Task<Event[]> FindSessionsByQrCode(string qrCode, DateTime nowFestival)
        {
            var matches = await ReadEventsByQrCode(qrCode);

            if (matches.Length == 0)
            {
                return [];
            }

            string day = FestivalDay.FromDayOfWeek(nowFestival.DayOfWeek);
            var nowLocal = TimeOnly.FromDateTime(nowFestival);

            // A session that runs past midnight still belongs to the day it started on: at 00:30
            // the guest standing in the 21:00-01:30 line is in yesterday's queue, and the calendar
            // has already moved on without them.
            var yesterdaysSessions = SessionsOn(matches, FestivalDay.FromDayOfWeek(nowFestival.AddDays(-1).DayOfWeek));

            if (ActivityDay.From(yesterdaysSessions)?.RunsPastMidnightInto(nowLocal) == true)
            {
                return yesterdaysSessions;
            }

            var todaysSessions = SessionsOn(matches, day);

            if (todaysSessions.Length > 0)
            {
                return todaysSessions;
            }

            // Nothing runs today - the festival has not started, or this activity is not on
            // today. Hand back the next day it does run rather than nothing at all, so
            // scanning a sign still works while the festival is being set up.
            var nextDay = matches
                .Select(e => FestivalDay.Normalize(e.Day))
                .Distinct()
                .OrderBy(d => FestivalDay.Rank(d))
                .FirstOrDefault(d => FestivalDay.Rank(d) >= FestivalDay.Rank(day));

            nextDay ??= matches
                .Select(e => FestivalDay.Normalize(e.Day))
                .OrderBy(d => FestivalDay.Rank(d))
                .First();

            return SessionsOn(matches, nextDay);
        }

        public async Task<Event[]> ReadSessionsForEvent(Event evt)
        {
            if (string.IsNullOrWhiteSpace(evt.QrCode))
            {
                // No code means no group: nothing else can resolve to this row's queue.
                return [evt];
            }

            var matches = await ReadEventsByQrCode(evt.QrCode);
            var sessions = SessionsOn(matches, FestivalDay.Normalize(evt.Day));

            // The row itself is always part of its own group, even if the table read raced a
            // code change and came back without it.
            return sessions.Any(e => e.RowKey == evt.RowKey) ? sessions : ActivityDay.InOrder([evt]);
        }

        /// <summary>The rows running on one festival day, in the order their sessions run.</summary>
        private static Event[] SessionsOn(Event[] matches, string normalizedDay) =>
            ActivityDay.InOrder(matches.Where(e => FestivalDay.Normalize(e.Day) == normalizedDay));

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
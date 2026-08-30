using Seniorfestival.Data.Models.Base;

namespace Seniorfestival.Data.Models
{
    [Table("Events")]
    public class Event : EntityBase
    {
        public string? Start { get; set; }
        public string? End { get; set; }
        public string? Day { get; set; }
        public string? Title { get; set; }
        public string? Description { get; set; }
        public string? PictureUrl { get; set; }
        public string? Links { get; set; }
        public string? Location { get; set; }
        public bool Public { get; set; }
        public string? QrCode { get; set; }
        public int? MinutesPerPerson { get; set; }

        // Timestamp of the most recently completed queue ticket, used to measure the gap to the next completion.
        public DateTimeOffset? LastServedAt { get; set; }

        // Up to the 3 most recent per-person service gaps in minutes, most recent first, comma separated.
        public string? RecentServiceMinutes { get; set; }

        // Comma separated open-close windows for the activity's day, e.g. "09:00-12:00,13:00-17:00,18:00-21:00".
        // One set per event - update it if the hours change from one festival day to the next.
        public string? OpeningHours { get; set; }

        public int Id()
        {
            return Convert.ToInt32(this.RowKey);

        }

        public int EstimateMinutesPerPerson(int fallbackMinutesPerPerson)
        {
            var recentValues = (RecentServiceMinutes ?? "")
                .Split(',', StringSplitOptions.RemoveEmptyEntries)
                .Select(v => int.TryParse(v, out var minutes) ? minutes : (int?)null)
                .Where(minutes => minutes.HasValue)
                .Select(minutes => minutes!.Value)
                .ToArray();

            if (recentValues.Length > 0)
            {
                return (int)Math.Round(recentValues.Average());
            }

            return MinutesPerPerson ?? fallbackMinutesPerPerson;
        }

        // Adjusts a raw queue-processing wait (position * minutes-per-person) so it doesn't count
        // time the activity is closed - e.g. a lunch break between two opening windows.
        public int EstimateWaitMinutes(int rawQueueWaitMinutes, TimeOnly nowLocal)
        {
            var windows = OpeningHoursCalculator.Parse(OpeningHours);
            if (windows.Length == 0)
            {
                return rawQueueWaitMinutes;
            }

            OpeningHoursCalculator.TryAddOpenMinutes(nowLocal, windows, rawQueueWaitMinutes, out var servedAt);
            var minutes = (servedAt - nowLocal).TotalMinutes;

            if (minutes < 0)
            {
                // Opening hours are same-day only; guard against wrapping past midnight.
                minutes += 24 * 60;
            }

            return Math.Max(0, (int)Math.Round(minutes));
        }

        // True if joining the queue right now would push a person's turn past the last open
        // window for today - i.e. there are no more available spots left to hand out.
        public bool WouldExceedOpeningHours(int rawQueueWaitMinutes, TimeOnly nowLocal)
        {
            var windows = OpeningHoursCalculator.Parse(OpeningHours);
            if (windows.Length == 0)
            {
                return false;
            }

            return !OpeningHoursCalculator.TryAddOpenMinutes(nowLocal, windows, rawQueueWaitMinutes, out _);
        }
    }
}
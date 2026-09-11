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

    }
}
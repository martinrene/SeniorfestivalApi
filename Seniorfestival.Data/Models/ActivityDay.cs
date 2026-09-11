using System.Globalization;

namespace Seniorfestival.Data.Models
{
    /// <summary>
    /// One session of an activity - a row's Start-End - placed on the timeline of its festival
    /// day. A session may run past midnight (21:00-01:30); the timeline then keeps counting past
    /// 24:00, so <see cref="EndMinutes"/> lands beyond a full day.
    /// </summary>
    public readonly struct SessionWindow
    {
        public const int MinutesPerDay = 24 * 60;

        private static readonly string[] TimeFormats = ["HH:mm", "H:mm", "HH:mm:ss", "H:mm:ss"];

        public TimeOnly Start { get; }
        public TimeOnly End { get; }

        public SessionWindow(TimeOnly start, TimeOnly end)
        {
            Start = start;
            End = end;
        }

        public int StartMinutes => ToMinutes(Start);

        // A close that is not after the open means the session runs into the next day.
        public int EndMinutes
        {
            get
            {
                int end = ToMinutes(End);
                return end > StartMinutes ? end : end + MinutesPerDay;
            }
        }

        /// <summary>
        /// The window of a row whose sheet times are missing or unreadable, or that starts and
        /// ends at the same minute - none of which say anything about when the activity is open.
        /// </summary>
        public static SessionWindow? FromEvent(Event evt)
        {
            if (!TryParseTime(evt.Start, out var start) || !TryParseTime(evt.End, out var end) || start == end)
            {
                return null;
            }

            return new SessionWindow(start, end);
        }

        // `minute` is a position on the day's timeline, so it may be >= MinutesPerDay.
        public bool ContainsMinute(int minute) => minute >= StartMinutes && minute < EndMinutes;

        // A clock time can sit in this session either on the day it started or - for a session
        // running past midnight - in the small hours that follow.
        public bool Contains(TimeOnly time)
        {
            int minute = ToMinutes(time);
            return ContainsMinute(minute) || ContainsMinute(minute + MinutesPerDay);
        }

        public override string ToString() => $"{Format(Start)}-{Format(End)}";

        internal static int ToMinutes(TimeOnly time) => (int)time.ToTimeSpan().TotalMinutes;

        internal static bool TryParseTime(string? value, out TimeOnly time) =>
            TimeOnly.TryParseExact(
                (value ?? "").Trim().Replace('.', ':'),
                TimeFormats,
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out time);

        private static string Format(TimeOnly time) => time.ToString("HH\\:mm", CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// The sessions one QR code covers on a single festival day. They share a single queue: it is
    /// one physical line at the activity, so a guest who joins during the morning session can have
    /// their turn come up in the evening one. The tickets live on <see cref="Host"/>, and the wait
    /// estimate counts only the minutes the activity is actually running.
    /// </summary>
    public sealed class ActivityDay
    {
        /// <summary>Every row in the group, the order they run in.</summary>
        public Event[] Sessions { get; }

        /// <summary>
        /// The row the shared queue's tickets live on - the day's first session. Any of the
        /// group's rows resolves to it, so a staff screen or admin page opened on the evening
        /// session works on the same queue as the morning one.
        /// </summary>
        public Event Host => Sessions[0];

        /// <summary>The sessions with usable times, earliest first.</summary>
        public SessionWindow[] Windows { get; }

        private ActivityDay(Event[] sessions, SessionWindow[] windows)
        {
            Sessions = sessions;
            Windows = windows;
        }

        /// <summary>Null when there are no rows at all - there is then no queue to speak of.</summary>
        public static ActivityDay? From(IEnumerable<Event> sessions)
        {
            var ordered = InOrder(sessions);

            if (ordered.Length == 0)
            {
                return null;
            }

            var windows = ordered
                .Select(SessionWindow.FromEvent)
                .Where(w => w.HasValue)
                .Select(w => w!.Value)
                .OrderBy(w => w.StartMinutes)
                .ToArray();

            return new ActivityDay(ordered, windows);
        }

        /// <summary>
        /// Earliest session first. Rows the sheet gave no readable start time sort last, so a
        /// half-filled row never becomes the host while a real session is available.
        /// </summary>
        public static Event[] InOrder(IEnumerable<Event> sessions) => sessions
            .OrderBy(e => SessionWindow.TryParseTime(e.Start, out var start)
                ? SessionWindow.ToMinutes(start)
                : int.MaxValue)
            .ThenBy(e => e.RowKey, StringComparer.Ordinal)
            .ToArray();

        /// <summary>
        /// True when <paramref name="time"/> falls in the tail of a session that started the day
        /// before - the small hours of a 21:00-01:30 session. That is the one case where the
        /// queue a guest is standing in belongs to yesterday's rows rather than today's.
        /// </summary>
        public bool RunsPastMidnightInto(TimeOnly time)
        {
            int minute = SessionWindow.ToMinutes(time) + SessionWindow.MinutesPerDay;

            return Windows.Any(w => w.ContainsMinute(minute));
        }

        /// <summary>"10:00-12:00" for each session with usable times, for the admin screens.</summary>
        public string[] SessionTimes => Windows.Select(w => w.ToString()).ToArray();

        /// <summary>
        /// Adjusts a raw queue-processing wait (position * minutes-per-person) so it doesn't count
        /// the time between the day's sessions - the queue stands still while the activity is not
        /// running. Falls back to the raw wait when no session has usable times.
        /// </summary>
        public int EstimateWaitMinutes(int rawQueueWaitMinutes, TimeOnly nowLocal)
        {
            if (Windows.Length == 0)
            {
                return Math.Max(0, rawQueueWaitMinutes);
            }

            int from = ToTimeline(nowLocal);
            // Even when the turn does not fit, `servedAt` is parked at the day's last close,
            // which is as long as anyone still waiting can be told to wait.
            TryAddRunningMinutes(from, rawQueueWaitMinutes, out int servedAt);

            return Math.Max(0, servedAt - from);
        }

        /// <summary>
        /// True if joining right now would put the person's turn after the last session of the day
        /// has ended - i.e. there are no more spots left to hand out. Always false when no session
        /// has usable times: nothing then says when the activity stops.
        /// </summary>
        public bool WouldRunPastLastSession(int rawQueueWaitMinutes, TimeOnly nowLocal) =>
            Windows.Length > 0 && !TryAddRunningMinutes(ToTimeline(nowLocal), rawQueueWaitMinutes, out _);

        /// <summary>
        /// Advances `from` by `minutesToAdd` of actual running time, skipping the gaps between
        /// sessions. False - with `servedAt` parked at the last session's end - when the day does
        /// not have that much running time left.
        /// </summary>
        private bool TryAddRunningMinutes(int from, int minutesToAdd, out int servedAt)
        {
            int lastClose = LastClose();

            if (from >= lastClose)
            {
                // The day's last session is over; nothing more happens today.
                servedAt = lastClose;
                return false;
            }

            int current = SkipToRunning(from);
            int remaining = minutesToAdd;

            while (remaining > 0)
            {
                var session = FindSession(current);

                if (session == null)
                {
                    // No session left to be served in - park at closing time rather than rolling
                    // the turn into the next festival day.
                    servedAt = lastClose;
                    return false;
                }

                int availableMinutes = session.Value.EndMinutes - current;

                if (remaining <= availableMinutes)
                {
                    servedAt = current + remaining;
                    return true;
                }

                remaining -= availableMinutes;
                current = SkipToRunning(session.Value.EndMinutes);
            }

            servedAt = current;
            return true;
        }

        /// <summary>
        /// Places a clock time on the day's timeline. A time like 00:30 belongs to the tail of a
        /// session that started the evening before, not to the start of a fresh day.
        /// </summary>
        private int ToTimeline(TimeOnly time)
        {
            int minute = SessionWindow.ToMinutes(time);

            if (FindSession(minute) != null)
            {
                return minute;
            }

            return FindSession(minute + SessionWindow.MinutesPerDay) != null
                ? minute + SessionWindow.MinutesPerDay
                : minute;
        }

        private SessionWindow? FindSession(int minute)
        {
            foreach (var window in Windows)
            {
                if (window.ContainsMinute(minute))
                {
                    return window;
                }
            }

            return null;
        }

        private int SkipToRunning(int minute)
        {
            if (FindSession(minute) != null)
            {
                return minute;
            }

            // Windows is sorted by start, so the first session starting after `minute` is the
            // next moment the activity is running.
            foreach (var window in Windows)
            {
                if (window.StartMinutes > minute)
                {
                    return window.StartMinutes;
                }
            }

            return minute;
        }

        private int LastClose()
        {
            int last = Windows[0].EndMinutes;

            foreach (var window in Windows)
            {
                if (window.EndMinutes > last)
                {
                    last = window.EndMinutes;
                }
            }

            return last;
        }
    }
}

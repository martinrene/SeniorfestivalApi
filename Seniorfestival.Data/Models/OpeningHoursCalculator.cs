using System.Globalization;

namespace Seniorfestival.Data.Models
{
    // A single open-close window on an activity's operating day, e.g. 09:00-12:00.
    public readonly struct OpeningHoursWindow
    {
        public TimeOnly Start { get; }
        public TimeOnly End { get; }

        public OpeningHoursWindow(TimeOnly start, TimeOnly end)
        {
            Start = start;
            End = end;
        }

        public bool Contains(TimeOnly time) => time >= Start && time < End;
    }

    public static class OpeningHoursCalculator
    {
        // Parses a comma separated list of "HH:mm-HH:mm" windows, e.g. "09:00-12:00,13:00-17:00,18:00-21:00".
        public static OpeningHoursWindow[] Parse(string? openingHours)
        {
            if (string.IsNullOrWhiteSpace(openingHours))
            {
                return [];
            }

            return openingHours
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(ParseWindow)
                .Where(w => w.HasValue)
                .Select(w => w!.Value)
                .OrderBy(w => w.Start)
                .ToArray();
        }

        private static OpeningHoursWindow? ParseWindow(string window)
        {
            var parts = window.Split('-', 2);
            if (parts.Length != 2)
            {
                return null;
            }

            if (!TimeOnly.TryParseExact(parts[0].Trim(), "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out var start) ||
                !TimeOnly.TryParseExact(parts[1].Trim(), "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out var end))
            {
                return null;
            }

            return new OpeningHoursWindow(start, end);
        }

        // Advances `from` by `minutesToAdd` of actual open time, skipping over any closed gaps
        // (before the first window, between windows, or after the last one for the day).
        // Returns false - with `result` parked at the last window's close time - if there isn't
        // enough remaining open time today to fit `minutesToAdd`.
        public static bool TryAddOpenMinutes(TimeOnly from, OpeningHoursWindow[] windows, int minutesToAdd, out TimeOnly result)
        {
            if (windows.Length == 0)
            {
                result = from.AddMinutes(minutesToAdd);
                return true;
            }

            var current = SkipToOpen(from, windows);
            var remaining = minutesToAdd;

            while (remaining > 0)
            {
                OpeningHoursWindow? activeWindow = null;
                foreach (var window in windows)
                {
                    if (window.Contains(current))
                    {
                        activeWindow = window;
                        break;
                    }
                }

                if (activeWindow == null)
                {
                    // No more open windows today - park at closing time rather than rolling into tomorrow.
                    result = windows[^1].End;
                    return false;
                }

                var availableMinutes = (activeWindow.Value.End - current).TotalMinutes;

                if (remaining <= availableMinutes)
                {
                    result = current.AddMinutes(remaining);
                    return true;
                }

                remaining -= (int)availableMinutes;
                current = SkipToOpen(activeWindow.Value.End, windows);
            }

            result = current;
            return true;
        }

        private static TimeOnly SkipToOpen(TimeOnly time, OpeningHoursWindow[] windows)
        {
            foreach (var window in windows)
            {
                if (window.Contains(time))
                {
                    return time;
                }
            }

            // windows is sorted by Start, so the first one starting after `time` is the next open moment.
            foreach (var window in windows)
            {
                if (window.Start > time)
                {
                    return window.Start;
                }
            }

            return time;
        }
    }
}

namespace Seniorfestival.Data.Models
{
    /// <summary>
    /// The festival's three days in the shape <see cref="Event.Day"/> stores them:
    /// lowercase and without Danish letters ("lordag", "sondag"), which is what
    /// EventsSheetSync writes from the sheet's Day column.
    /// </summary>
    public static class FestivalDay
    {
        public const string Friday = "fredag";
        public const string Saturday = "lordag";
        public const string Sunday = "sondag";

        /// <summary>
        /// Chronological. Also the order to walk an activity's days in when today is not
        /// one of them.
        /// </summary>
        public static readonly string[] Order = [Friday, Saturday, Sunday];

        /// <summary>
        /// Folds case and the Danish letters, so a sheet row spelled "Lørdag" matches a
        /// stored "lordag".
        /// </summary>
        public static string Normalize(string? day) =>
            (day ?? "")
                .Trim()
                .ToLowerInvariant()
                .Replace("ø", "o")
                .Replace("å", "a")
                .Replace("æ", "a");

        /// <summary>
        /// Empty when the date is not a festival day - the festival runs Friday to Sunday.
        /// </summary>
        public static string FromDayOfWeek(DayOfWeek dayOfWeek) => dayOfWeek switch
        {
            DayOfWeek.Friday => Friday,
            DayOfWeek.Saturday => Saturday,
            DayOfWeek.Sunday => Sunday,
            _ => "",
        };

        /// <summary>
        /// Position in <see cref="Order"/>, and past the end for anything else, so an
        /// unrecognised day sorts after the three festival days rather than before them.
        /// </summary>
        public static int Rank(string? day)
        {
            int index = Array.IndexOf(Order, Normalize(day));

            return index >= 0 ? index : Order.Length;
        }
    }
}

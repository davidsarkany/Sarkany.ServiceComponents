using System;
using System.Collections.Generic;
using System.Linq;

namespace ServiceComponents.Core.Extensions
{
    /// <summary>
    /// Operations on half-open time intervals [Start, Finish).
    /// </summary>
    public static class TimeIntervalExtensions
    {
        public static bool IntersectsWith(this ITimeInterval interval, ITimeInterval other)
        {
            return interval.Finish > other.Start && interval.Start < other.Finish;
        }

        public static bool Contains(this ITimeInterval interval, DateTime dateTime)
        {
            return interval.Start <= dateTime && interval.Finish > dateTime;
        }

        public static bool Contains(this ITimeInterval interval, ITimeInterval other)
        {
            return other.Start >= interval.Start && other.Finish <= interval.Finish;
        }

        public static bool StartsBefore(this ITimeInterval interval, ITimeInterval other)
        {
            return interval.Start < other.Start;
        }

        /// <summary>
        /// Merges overlapping and touching intervals; the result is ordered by start.
        /// </summary>
        public static IEnumerable<ITimeInterval> Simplify(this IEnumerable<ITimeInterval> source)
        {
            var intervals = source as ITimeInterval[] ?? source.ToArray();

            if (intervals.Length == 0) {
                yield break;
            }

            if (intervals.Length == 1) {
                yield return intervals[0];
                yield break;
            }

            // Starts sort before finishes at the same instant, so touching intervals are merged.
            var changes = intervals
                .SelectMany(x => new[] { (time: x.Start, delta: 1), (time: x.Finish, delta: -1) })
                .OrderBy(x => x.time)
                .ThenByDescending(x => x.delta);

            var open = 0;
            var start = default(DateTime);
            foreach (var (time, delta) in changes) {
                if (open == 0) {
                    start = time;
                }

                open += delta;
                if (open == 0) {
                    yield return new TimeInterval(start, time);
                }
            }
        }

        public static ITimeInterval Week(this DateTime date)
        {
            var firstDay = date.Date.StartOfWeek();
            return new TimeInterval(firstDay, firstDay.AddDays(7));
        }

        public static ITimeInterval WorkWeek(this DateTime date)
        {
            var monday = date.Date.StartOfWeek(DayOfWeek.Monday);
            return new TimeInterval(monday, monday.AddDays(5));
        }

        public static ITimeInterval CalendarMonth(DateTime date)
        {
            var start = new DateTime(date.Year, date.Month, 1);
            return new TimeInterval(start, start.AddMonths(1));
        }

        public static ITimeInterval CalendarYear(this DateTime date)
        {
            return new TimeInterval(new DateTime(date.Year, 1, 1), new DateTime(date.Year + 1, 1, 1));
        }

        public static double DurationRateOf(this ITimeInterval part, ITimeInterval whole)
        {
            return (double)part.Duration.Ticks / whole.Duration.Ticks;
        }
    }
}

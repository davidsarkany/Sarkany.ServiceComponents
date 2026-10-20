using System;
using System.Linq;
using ServiceComponents.Core;
using ServiceComponents.Core.Extensions;
using Xunit;

namespace ServiceComponents.UnitTests.Core
{
    public class TimeIntervalTests
    {
        private static readonly DateTime T0 = new DateTime(2024, 1, 1, 8, 0, 0);

        [Fact]
        public void Constructor_orders_start_and_finish()
        {
            var interval = new TimeInterval(T0.AddHours(2), T0);

            Assert.Equal(T0, interval.Start);
            Assert.Equal(T0.AddHours(2), interval.Finish);
            Assert.Equal(TimeSpan.FromHours(2), interval.Duration);
        }

        [Fact]
        public void Duration_constructors_compute_bounds()
        {
            var fromStart = new TimeInterval(T0, TimeSpan.FromMinutes(30));
            var toFinish = new TimeInterval(TimeSpan.FromMinutes(30), T0);

            Assert.Equal(T0.AddMinutes(30), fromStart.Finish);
            Assert.Equal(T0.AddMinutes(-30), toFinish.Start);
        }

        [Fact]
        public void Open_ended_interval_finishes_at_max_value()
        {
            Assert.Equal(DateTime.MaxValue, new TimeInterval(T0).Finish);
        }

        [Fact]
        public void Deconstruct_returns_bounds()
        {
            var (start, finish) = new TimeInterval(T0, T0.AddDays(1));

            Assert.Equal(T0, start);
            Assert.Equal(T0.AddDays(1), finish);
        }

        [Fact]
        public void Value_equality_is_based_on_bounds()
        {
            var a = new TimeInterval(T0, T0.AddHours(1));
            var b = new TimeInterval(T0.AddHours(1), T0);
            var c = new TimeInterval(T0, T0.AddHours(2));

            Assert.Equal(a, b);
            Assert.True(a == b);
            Assert.True(a != c);
            Assert.Equal(a.GetHashCode(), b.GetHashCode());
        }

        [Fact]
        public void ToString_uses_invariant_culture()
        {
            var interval = new TimeInterval(T0, T0.AddHours(1));

            Assert.Equal("01/01/2024 08:00:00-01/01/2024 09:00:00", interval.ToString());
        }

        [Theory]
        [InlineData(0, 2, 1, 3, true)]
        [InlineData(0, 1, 1, 2, false)]
        [InlineData(0, 1, 2, 3, false)]
        [InlineData(1, 2, 0, 3, true)]
        public void IntersectsWith(int s1, int f1, int s2, int f2, bool expected)
        {
            var a = new TimeInterval(T0.AddHours(s1), T0.AddHours(f1));
            var b = new TimeInterval(T0.AddHours(s2), T0.AddHours(f2));

            Assert.Equal(expected, a.IntersectsWith(b));
        }

        [Fact]
        public void Contains_interval()
        {
            var outer = new TimeInterval(T0, T0.AddHours(4));

            Assert.True(outer.Contains(new TimeInterval(T0.AddHours(1), T0.AddHours(2))));
            Assert.True(outer.Contains(outer));
            Assert.False(outer.Contains(new TimeInterval(T0.AddHours(3), T0.AddHours(5))));
        }

        [Fact]
        public void Contains_datetime_of_half_open_interval()
        {
            var interval = new TimeInterval(T0, T0.AddHours(4));

            // Fixed (known issue #10): the start comparison used to be reversed.
            Assert.True(interval.Contains(T0.AddHours(1)));
            Assert.False(interval.Contains(T0.AddHours(-1)));
            Assert.True(interval.Contains(T0));
            Assert.False(interval.Contains(T0.AddHours(4)));
        }

        [Fact]
        public void StartsBefore()
        {
            var a = new TimeInterval(T0, T0.AddHours(1));
            var b = new TimeInterval(T0.AddMinutes(1), T0.AddHours(1));

            Assert.True(a.StartsBefore(b));
            Assert.False(b.StartsBefore(a));
        }

        [Fact]
        public void Simplify_merges_overlapping_intervals()
        {
            var intervals = new ITimeInterval[] {
                new TimeInterval(T0, T0.AddHours(2)),
                new TimeInterval(T0.AddHours(1), T0.AddHours(3)),
                new TimeInterval(T0.AddHours(5), T0.AddHours(6))
            };

            var result = intervals.Simplify().ToArray();

            Assert.Equal(new ITimeInterval[] {
                new TimeInterval(T0, T0.AddHours(3)),
                new TimeInterval(T0.AddHours(5), T0.AddHours(6))
            }, result);
        }

        [Fact]
        public void Simplify_handles_empty_and_single()
        {
            Assert.Empty(Array.Empty<ITimeInterval>().Simplify());

            var single = new TimeInterval(T0, T0.AddHours(1));
            Assert.Same(single, new ITimeInterval[] { single }.Simplify().Single());
        }

        [Fact]
        public void Simplify_merges_nested_intervals()
        {
            var intervals = new ITimeInterval[] {
                new TimeInterval(T0, T0.AddHours(10)),
                new TimeInterval(T0.AddHours(1), T0.AddHours(2))
            };

            Assert.Equal(new TimeInterval(T0, T0.AddHours(10)), intervals.Simplify().Single());
        }

        [Fact]
        public void CalendarMonth_and_year()
        {
            var date = new DateTime(2024, 2, 15, 13, 0, 0);

            Assert.Equal(new TimeInterval(new DateTime(2024, 2, 1), new DateTime(2024, 3, 1)), TimeIntervalExtensions.CalendarMonth(date));
            Assert.Equal(new TimeInterval(new DateTime(2024, 1, 1), new DateTime(2025, 1, 1)), date.CalendarYear());
        }

        [Fact]
        public void DurationRateOf_is_a_fraction()
        {
            var whole = new TimeInterval(T0, T0.AddHours(4));
            var half = new TimeInterval(T0, T0.AddHours(2));

            // Fixed (known issue #10): integer division used to truncate partial rates to 0.
            Assert.Equal(0.5d, half.DurationRateOf(whole));
            Assert.Equal(1d, whole.DurationRateOf(whole));
        }

        [Fact]
        public void Simplify_merges_touching_intervals_regardless_of_order()
        {
            var first = new TimeInterval(T0, T0.AddHours(1));
            var second = new TimeInterval(T0.AddHours(1), T0.AddHours(2));

            // Fixed (known issue #10): the result used to depend on the input order.
            Assert.Equal(new TimeInterval(T0, T0.AddHours(2)), new ITimeInterval[] { first, second }.Simplify().Single());
            Assert.Equal(new TimeInterval(T0, T0.AddHours(2)), new ITimeInterval[] { second, first }.Simplify().Single());
        }
    }
}

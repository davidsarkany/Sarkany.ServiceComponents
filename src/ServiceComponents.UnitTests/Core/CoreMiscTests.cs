using System;
using System.Linq;
using System.Reflection;
using Moq;
using ServiceComponents.Core;
using ServiceComponents.Core.Exceptions;
using ServiceComponents.Core.Extensions;
using ServiceComponents.Core.Services;
using Xunit;

namespace ServiceComponents.UnitTests.Core
{
    public class CoreMiscTests
    {
        [Fact]
        public void Random_uses_requested_length_and_character_set()
        {
            var value = StringHelper.Random(64, RandomCharacterSet.Numbers);

            Assert.Equal(64, value.Length);
            Assert.All(value, c => Assert.Contains(c, RandomCharacterSet.Numbers));
        }

        [Fact]
        public void Random_postfix_and_prefix()
        {
            var postfixed = "queue".AddRandomPostfix();
            var prefixed = "queue".AddRandomPrefix('_', 4, RandomCharacterSet.Letters);

            Assert.Matches("^queue-[a-zA-Z0-9]{10}$", postfixed);
            Assert.Matches("^[a-zA-Z]{4}_queue$", prefixed);
        }

        [Fact]
        public void DisplayName_and_versionless_qualified_name()
        {
            var obj = new DescriptionAttribute("n", "d");

            Assert.Equal("DescriptionAttribute", obj.DisplayName());
            Assert.Equal("ServiceComponents.Core.DescriptionAttribute, ServiceComponents.Core", obj.AssemblyVersionlessQualifiedName());
        }

        [Fact]
        public void Description_attribute_holds_values()
        {
            var attribute = new DescriptionAttribute("name", "description");

            Assert.Equal("name", attribute.Name);
            Assert.Equal("description", attribute.Description);
        }

        [Fact]
        public void Version_helpers_read_assembly_metadata()
        {
            var assembly = typeof(Enumeration).Assembly;

            Assert.Equal(assembly.GetName().Version.ToString(), assembly.AssemblyVersion());
            Assert.False(string.IsNullOrEmpty(assembly.FileVersion()));
            Assert.False(string.IsNullOrEmpty(assembly.ProductVersion()));
        }

        [Fact]
        public void Clock_elapsed_since()
        {
            var clock = new Mock<IClock>();
            clock.SetupGet(c => c.UtcNow).Returns(new DateTime(2024, 1, 1, 12, 0, 0));

            Assert.Equal(TimeSpan.FromHours(2), clock.Object.ElapsedSince(new DateTime(2024, 1, 1, 10, 0, 0)));
        }

        [Fact]
        public void Computer_clock_returns_utc_now()
        {
            var before = DateTime.UtcNow;
            var now = new ComputerClock().UtcNow;

            Assert.Equal(DateTimeKind.Utc, now.Kind);
            Assert.InRange(now, before, DateTime.UtcNow);
        }

        [Fact]
        public void Business_exception_constructors()
        {
            var inner = new Exception("inner");

            Assert.Equal(0, new BusinessException().ErrorCode);
            Assert.Equal("m", new BusinessException("m").Message);
            Assert.Same(inner, new BusinessException("m", inner).InnerException);
            Assert.Equal(7, new BusinessException(7, "m").ErrorCode);
            var full = new BusinessException(8, "m", inner);
            Assert.Equal(8, full.ErrorCode);
            Assert.Same(inner, full.InnerException);
        }

        [Fact]
        public void Derived_business_exceptions()
        {
            var inner = new Exception();

            Assert.Equal(404, new NotFoundException("missing").ErrorCode);
            Assert.Equal(10, new NotFoundException(10, "missing").ErrorCode);
            Assert.Equal(0, new NotFoundException().ErrorCode);
            Assert.Same(inner, new NotFoundException("m", inner).InnerException);
            // Fixed: the constructor with an inner exception used to leave the error code at 0.
            Assert.Equal(404, new NotFoundException("m", inner).ErrorCode);

            Assert.Equal("c", new ConflictException("c").Message);
            Assert.NotNull(new ConflictException());
            Assert.Same(inner, new ConflictException("m", inner).InnerException);

            Assert.Equal("g", new GoneException("g").Message);
            Assert.NotNull(new GoneException());
            Assert.Same(inner, new GoneException("m", inner).InnerException);

            Assert.IsAssignableFrom<BusinessException>(new ConflictException());
            Assert.IsAssignableFrom<BusinessException>(new GoneException());
            Assert.IsAssignableFrom<BusinessException>(new NotFoundException());
        }

        [Theory]
        [InlineData(DayOfWeek.Monday, true)]
        [InlineData(DayOfWeek.Friday, true)]
        [InlineData(DayOfWeek.Saturday, false)]
        [InlineData(DayOfWeek.Sunday, false)]
        public void Weekday_and_holiday(DayOfWeek day, bool weekday)
        {
            // 2024-01-01 is a Monday
            var date = new DateTime(2024, 1, 1).AddDays(((int)day + 6) % 7);

            Assert.Equal(day, date.DayOfWeek);
            Assert.Equal(weekday, date.IsWeekday());
            Assert.Equal(!weekday, date.IsHoliday());
        }

        [Fact]
        public void Exceptions_have_protected_serialization_constructors()
        {
            var types = new[] { typeof(BusinessException), typeof(ConflictException), typeof(GoneException), typeof(NotFoundException) };

            Assert.All(types, type => Assert.NotNull(type
                .GetConstructors(BindingFlags.Instance | BindingFlags.NonPublic)
                .SingleOrDefault(c => c.GetParameters().Length == 2 && c.IsFamily)));
        }
    }
}

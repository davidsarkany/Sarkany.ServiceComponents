using System;
using System.Linq;
using ServiceComponents.Core;
using Xunit;

namespace ServiceComponents.UnitTests.Core
{
    public class EnumerationTests
    {
        public class Color : Enumeration
        {
            public static readonly Color Red = new Color(1, "Red");
            public static readonly Color Green = new Color(2, "Green");

            public Color() : base(0, null) { }
            private Color(int id, string name) : base(id, name) { }
        }

        public class Size : Enumeration
        {
            public static readonly Size Small = new Size(1, "Small");

            public Size() : base(0, null) { }
            private Size(int id, string name) : base(id, name) { }
        }

        [Fact]
        public void GetAll_returns_declared_static_fields()
        {
            Assert.Equal(new[] { Color.Red, Color.Green }, Enumeration.GetAll<Color>().ToArray());
        }

        [Fact]
        public void FromId_and_FromName()
        {
            Assert.Same(Color.Green, Enumeration.FromId<Color>(2));
            Assert.Same(Color.Red, Enumeration.FromName<Color>("Red"));
        }

        [Fact]
        public void Invalid_lookup_throws_ApplicationException()
        {
            var byId = Assert.Throws<ApplicationException>(() => Enumeration.FromId<Color>(42));
            var byName = Assert.Throws<ApplicationException>(() => Enumeration.FromName<Color>("Blue"));

            Assert.Equal($"'42' is not a valid ID in {typeof(Color)}", byId.Message);
            Assert.Equal($"'Blue' is not a valid name in {typeof(Color)}", byName.Message);
        }

        [Fact]
        public void Equality_requires_same_type_and_id()
        {
            Assert.Equal(Color.Red, Enumeration.FromId<Color>(1));
            Assert.False(Color.Red.Equals(Size.Small));
            Assert.False(Color.Red.Equals("Red"));
            Assert.Equal(1, Color.Red.GetHashCode());
        }

        public class WithOtherStaticField : Enumeration
        {
            public static readonly WithOtherStaticField One = new(1, "One");
            public static readonly string Description = "not an enumeration value";

            public WithOtherStaticField() : base(0, null) { }
            private WithOtherStaticField(int id, string name) : base(id, name) { }
        }

        [Fact]
        public void Other_static_fields_are_ignored()
        {
            // Fixed in 0.3: GetAll used to fail with InvalidCastException.
            Assert.Equal(new[] { WithOtherStaticField.One }, Enumeration.GetAll<WithOtherStaticField>());
        }

        [Fact]
        public void CompareTo_orders_by_id_and_ToString_returns_name()
        {
            Assert.True(Color.Red.CompareTo(Color.Green) < 0);
            Assert.Equal("Green", Color.Green.ToString());
        }
    }
}

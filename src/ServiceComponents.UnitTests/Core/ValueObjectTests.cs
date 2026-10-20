using System.Collections.Generic;
using ServiceComponents.Core;
using Xunit;

namespace ServiceComponents.UnitTests.Core
{
    public class ValueObjectTests
    {
        private class Money : ValueObject
        {
            public decimal Amount { get; }
            public string Currency { get; }

            public Money(decimal amount, string currency)
            {
                Amount = amount;
                Currency = currency;
            }

            protected override IEnumerable<object> GetAtomicValues()
            {
                yield return Amount;
                yield return Currency;
            }

            public static bool Eq(ValueObject a, ValueObject b) => EqualOperator(a, b);
            public static bool NotEq(ValueObject a, ValueObject b) => NotEqualOperator(a, b);
        }

        private class OtherMoney : Money
        {
            public OtherMoney(decimal amount, string currency) : base(amount, currency) { }
        }

        private class Values : ValueObject
        {
            private readonly object[] _values;

            public Values(params object[] values)
            {
                _values = values;
            }

            protected override IEnumerable<object> GetAtomicValues() => _values;
        }

        [Fact]
        public void Equal_values_are_equal()
        {
            var a = new Money(10, "HUF");
            var b = new Money(10, "HUF");

            Assert.True(a.Equals(b));
            Assert.True(a.Equals((ValueObject)b));
            Assert.True(a == b);
            Assert.False(a != b);
            Assert.Equal(a.GetHashCode(), b.GetHashCode());
        }

        [Fact]
        public void Different_values_are_not_equal()
        {
            Assert.NotEqual(new Money(10, "HUF"), new Money(11, "HUF"));
            Assert.NotEqual(new Money(10, "HUF"), new Money(10, "EUR"));
        }

        [Fact]
        public void Different_types_are_not_equal()
        {
            Assert.False(new Money(10, "HUF").Equals(new OtherMoney(10, "HUF")));
        }

        [Fact]
        public void Null_handling()
        {
            var a = new Money(10, "HUF");

            Assert.False(a.Equals(null));
            Assert.False(a == null);
            Assert.False(null == a);
            Assert.True((Money)null == null);
            Assert.True(Money.Eq(null, null));
            Assert.False(Money.Eq(a, null));
            Assert.True(Money.NotEq(a, null));
            Assert.True(Money.Eq(a, new Money(10, "HUF")));
        }

        [Fact]
        public void Null_atomic_values_are_compared()
        {
            Assert.Equal(new Money(1, null), new Money(1, null));
            Assert.NotEqual(new Money(1, null), new Money(1, "HUF"));
            Assert.NotEqual(new Money(1, "HUF"), new Money(1, null));
            Assert.Equal(new Money(1, null).GetHashCode(), new Money(1, null).GetHashCode());
        }

        [Fact]
        public void Different_value_counts_are_not_equal()
        {
            // Fixed in 0.3 (known issue #9): equality used to be asymmetric when the left side had one more value.
            Assert.False(new Values(1, 2).Equals(new Values(1)));
            Assert.False(new Values(1).Equals(new Values(1, 2)));
            Assert.False(new Values(1, 2, 3).Equals(new Values(1)));
        }

        [Fact]
        public void Empty_value_object_has_a_hash_code()
        {
            // Fixed in 0.3 (known issue #9): GetHashCode used to throw for an empty value list.
            Assert.Equal(new Values().GetHashCode(), new Values().GetHashCode());
            Assert.Equal(new Values(), new Values());
        }

        [Fact]
        public void GetCopy_returns_equal_but_distinct_instance()
        {
            var a = new Money(10, "HUF");
            var copy = a.GetCopy();

            Assert.NotSame(a, copy);
            Assert.Equal(a, copy);
        }
    }
}

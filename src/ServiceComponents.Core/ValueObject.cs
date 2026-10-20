using System;
using System.Collections.Generic;
using System.Linq;

namespace ServiceComponents.Core
{
    /// <summary>
    /// Base class for value objects: two instances of the same type are equal when their atomic values are equal, in order.
    /// </summary>
    public abstract class ValueObject : IEquatable<ValueObject>
    {
        protected static bool EqualOperator(ValueObject left, ValueObject right)
        {
            if (left is null ^ right is null) {
                return false;
            }

            return left is null || left.Equals(right);
        }

        protected static bool NotEqualOperator(ValueObject left, ValueObject right)
        {
            return !EqualOperator(left, right);
        }

        protected abstract IEnumerable<object> GetAtomicValues();

        public bool Equals(ValueObject other)
        {
            return Equals((object)other);
        }

        public override bool Equals(object obj)
        {
            if (obj == null || obj.GetType() != GetType()) {
                return false;
            }

            return GetAtomicValues().SequenceEqual(((ValueObject)obj).GetAtomicValues());
        }

        public static bool operator ==(ValueObject x, ValueObject y) => x is null ? y is null : x.Equals(y);

        public static bool operator !=(ValueObject x, ValueObject y) => !(x == y);

        public override int GetHashCode()
        {
            var hash = new HashCode();
            foreach (var value in GetAtomicValues()) {
                hash.Add(value);
            }

            return hash.ToHashCode();
        }

        public ValueObject GetCopy()
        {
            return MemberwiseClone() as ValueObject;
        }
    }
}

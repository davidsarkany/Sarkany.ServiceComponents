using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace ServiceComponents.Core
{
    /// <summary>
    /// Base class for enumeration classes: the values are the public static fields of type <c>T</c> declared on <c>T</c>.
    /// </summary>
    public abstract class Enumeration : IComparable
    {
        public string Name { get; }

        public int Id { get; }

        protected Enumeration(int id, string name)
        {
            Id = id;
            Name = name;
        }

        public override string ToString()
        {
            return Name;
        }

        public static IEnumerable<T> GetAll<T>() where T : Enumeration
        {
            return typeof(T).GetFields(BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly)
                .Where(field => typeof(T).IsAssignableFrom(field.FieldType))
                .Select(field => (T)field.GetValue(null));
        }

        public override bool Equals(object obj)
        {
            return obj is Enumeration other && GetType() == obj.GetType() && Id == other.Id;
        }

        public static T FromId<T>(int id) where T : Enumeration, new()
        {
            return Parse<T, int>(id, "ID", item => item.Id == id);
        }

        public static T FromName<T>(string name) where T : Enumeration, new()
        {
            return Parse<T, string>(name, "name", item => item.Name == name);
        }

        private static T Parse<T, TValue>(TValue value, string description, Func<T, bool> predicate) where T : Enumeration
        {
            return GetAll<T>().FirstOrDefault(predicate)
                ?? throw new ApplicationException($"'{value}' is not a valid {description} in {typeof(T)}");
        }

        public override int GetHashCode()
        {
            return Id;
        }

        public int CompareTo(object other) => Id.CompareTo(((Enumeration)other).Id);
    }
}

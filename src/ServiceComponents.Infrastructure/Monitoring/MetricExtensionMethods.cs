using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using ServiceComponents.Application.Monitoring;

namespace ServiceComponents.Infrastructure.Monitoring
{
    /// <summary>
    /// Reflection over metric classes: the class attribute gives the metric kind, name and help text, the public instance
    /// properties give the labels (named by <see cref="MetricFieldAttribute"/>, in declaration order).
    /// </summary>
    public static class MetricExtensionMethods
    {
        public static IEnumerable<string> MetricFields(this object obj)
        {
            return MetricDescriptor.For(obj.GetType()).LabelNames;
        }

        public static (string name, string title) MetricDescription(this object obj)
        {
            var descriptor = MetricDescriptor.For(obj.GetType());
            return (descriptor.Name, descriptor.Title);
        }

        /// <summary>Label values of the metric instance; null property values become empty strings.</summary>
        public static IEnumerable<string> MetricValues(this object obj)
        {
            return MetricDescriptor.For(obj.GetType()).LabelValues(obj);
        }
    }

    internal sealed class MetricDescriptor
    {
        private static readonly ConcurrentDictionary<Type, MetricDescriptor> Cache = new();

        private readonly Func<object, object>[] _getters;

        public MetricAttribute Kind { get; }
        public string Name { get; }
        public string Title { get; }
        public string[] LabelNames { get; }

        private MetricDescriptor(Type type)
        {
            var properties = type.GetProperties(BindingFlags.Public | BindingFlags.Instance);
            var description = type.GetCustomAttribute<MetricAttribute>();

            Kind = type.GetCustomAttributes(true).OfType<MetricAttribute>().FirstOrDefault();
            Name = description == null ? type.Name : description.Name;
            Title = description == null ? type.Namespace : description.Title;
            LabelNames = properties.Select(info => info.GetCustomAttribute<MetricFieldAttribute>()?.Name ?? info.Name).ToArray();
            _getters = properties.Select(CompileGetter).ToArray();
        }

        public static MetricDescriptor For(Type type) => Cache.GetOrAdd(type, t => new MetricDescriptor(t));

        public string[] LabelValues(object metric)
        {
            var values = new string[_getters.Length];
            for (var i = 0; i < values.Length; i++) {
                values[i] = _getters[i](metric)?.ToString() ?? string.Empty;
            }

            return values;
        }

        public string KindName => Kind?.GetType().Name ?? "(no metric attribute)";

        private static Func<object, object> CompileGetter(PropertyInfo property)
        {
            if (property.GetIndexParameters().Length > 0) {
                return property.GetValue;
            }

            var instance = Expression.Parameter(typeof(object));
            var body = Expression.Convert(Expression.Property(Expression.Convert(instance, property.DeclaringType!), property), typeof(object));
            return Expression.Lambda<Func<object, object>>(body, instance).Compile();
        }
    }
}

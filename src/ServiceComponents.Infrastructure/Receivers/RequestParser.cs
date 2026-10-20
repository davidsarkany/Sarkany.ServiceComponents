using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json.Serialization;
using ServiceComponents.Api.Mediator;

namespace ServiceComponents.Infrastructure.Receivers
{
    /// <summary>
    /// Deserializes incoming payloads (HTTP and RabbitMQ) into commands, queries and events.
    /// </summary>
    /// <remarks>
    /// With a type name (<c>domain-type</c> header / <c>Type</c> property) the payload is deserialized into that type with
    /// default settings, but only when it is a command, query or event. Without a type name the payload must carry a
    /// Newtonsoft <c>$type</c>; those type names are restricted to message types, the assemblies passed to
    /// <see cref="AllowTypesFrom"/> (<c>AddMediator</c> passes its assemblies) and plain .NET value and collection types.
    /// </remarks>
    public class RequestParser
    {
        private static readonly ConcurrentDictionary<string, Type> TypeNames = new();
        private static readonly JsonSerializerSettings EmbeddedTypeSettings = new() {
            TypeNameHandling = TypeNameHandling.All,
            TypeNameAssemblyFormatHandling = TypeNameAssemblyFormatHandling.Simple,
            SerializationBinder = new AllowListBinder()
        };

        /// <summary>
        /// Allows the types of the given assemblies in <c>$type</c> annotated payloads (e.g. DTOs nested in messages).
        /// </summary>
        public static void AllowTypesFrom(params Assembly[] assemblies)
        {
            foreach (var assembly in assemblies) {
                AllowListBinder.AllowedAssemblies.TryAdd(assembly, true);
            }
        }

        public async Task ParseAsync(
            string payload,
            string typeName,
            Func<ICommand, CancellationToken, Task> commandAction,
            Func<IQuery, CancellationToken, Task> queryAction,
            Func<IEvent, CancellationToken, Task> eventAction,
            Func<string, CancellationToken, Task> messageAction = null,
            CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrEmpty(payload)) {
                payload = "{}";
            }

            object request;
            try {
                request = string.IsNullOrEmpty(typeName)
                    ? JsonConvert.DeserializeObject(payload, EmbeddedTypeSettings) ?? throw new InvalidOperationException("Unable to deserialize request")
                    : DeserializeTyped(payload, typeName);
            }
            catch (JsonReaderException) {
                request = string.Empty;
            }
            catch (JsonSerializationException exception) when (exception.GetBaseException() is TypeNotAllowedException notAllowed) {
                throw new InvalidOperationException(notAllowed.Message, exception);
            }

            switch (request) {
                case ICommand command:
                    await commandAction(command, cancellationToken);
                    break;

                case IQuery query:
                    await queryAction(query, cancellationToken);
                    break;

                case IEvent @event:
                    await eventAction(@event, cancellationToken);
                    break;

                default:
                    if (messageAction != null) {
                        await messageAction(payload, cancellationToken);
                        break;
                    }

                    throw new InvalidOperationException($"Unknown request type: {(request is UnknownRequest unknown ? unknown.TypeName : request.GetType().FullName)} (not a command, query or event)");
            }
        }

        private static object DeserializeTyped(string payload, string typeName)
        {
            var type = ResolveType(typeName);
            if (type == null || AllowListBinder.IsMessage(type)) {
                // An unknown type name deserializes into a JObject, as Type.GetType returns null.
                return JsonConvert.DeserializeObject(payload, type);
            }

            // Other types are never instantiated from the payload; only the JSON is validated.
            JToken.Parse(payload);
            return new UnknownRequest(type.FullName);
        }

        private static Type ResolveType(string typeName)
        {
            if (TypeNames.TryGetValue(typeName, out var cached)) {
                return cached;
            }

            var type = Type.GetType(typeName);
            if (type != null) {
                TypeNames.TryAdd(typeName, type);
            }

            return type;
        }

        private sealed record UnknownRequest(string TypeName);

        private sealed class TypeNotAllowedException : Exception
        {
            public TypeNotAllowedException(string typeName) : base($"Type '{typeName}' is not allowed in requests") { }
        }

        private sealed class AllowListBinder : DefaultSerializationBinder
        {
            public static readonly ConcurrentDictionary<Assembly, bool> AllowedAssemblies = new();

            private static readonly HashSet<Type> SystemTypes = new() {
                typeof(string), typeof(decimal), typeof(DateTime), typeof(DateTimeOffset), typeof(DateOnly), typeof(TimeOnly),
                typeof(TimeSpan), typeof(Guid), typeof(Uri), typeof(object)
            };

            private static readonly HashSet<Type> GenericDefinitions = new() {
                typeof(Nullable<>), typeof(List<>), typeof(IList<>), typeof(ICollection<>), typeof(IEnumerable<>), typeof(IReadOnlyList<>),
                typeof(IReadOnlyCollection<>), typeof(HashSet<>), typeof(ISet<>), typeof(Dictionary<,>), typeof(IDictionary<,>),
                typeof(IReadOnlyDictionary<,>), typeof(KeyValuePair<,>), typeof(Collection<>), typeof(ReadOnlyCollection<>)
            };

            public static bool IsMessage(Type type) =>
                typeof(ICommand).IsAssignableFrom(type) || typeof(IQuery).IsAssignableFrom(type) || typeof(IEvent).IsAssignableFrom(type);

            private static readonly ConcurrentDictionary<(string assemblyName, string typeName), Type> Bound = new();

            public override Type BindToType(string assemblyName, string typeName)
            {
                if (Bound.TryGetValue((assemblyName, typeName), out var bound)) {
                    return bound;
                }

                var type = Resolve(assemblyName, typeName);
                Bound.TryAdd((assemblyName, typeName), type);
                return type;
            }

            private Type Resolve(string assemblyName, string typeName)
            {
                var type = base.BindToType(assemblyName, typeName);
                if (IsMessage(type)) {
                    // Types declared next to a message (e.g. its DTOs) are allowed too.
                    AllowedAssemblies.TryAdd(type.Assembly, true);
                    return type;
                }

                return IsAllowed(type) ? type : throw new TypeNotAllowedException($"{typeName}, {assemblyName}");
            }

            private static bool IsAllowed(Type type)
            {
                if (type.IsArray) {
                    return IsAllowed(type.GetElementType());
                }

                if (type.IsGenericType && !type.IsGenericTypeDefinition) {
                    return IsAllowed(type.GetGenericTypeDefinition()) && Array.TrueForAll(type.GetGenericArguments(), IsAllowed);
                }

                return type.IsPrimitive
                       || type.IsEnum
                       || SystemTypes.Contains(type)
                       || GenericDefinitions.Contains(type)
                       || typeof(JToken).IsAssignableFrom(type)
                       || IsMessage(type)
                       || AllowedAssemblies.ContainsKey(type.Assembly);
            }
        }
    }
}

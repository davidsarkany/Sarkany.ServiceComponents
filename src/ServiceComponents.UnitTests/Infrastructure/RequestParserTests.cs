using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ServiceComponents.Api.Mediator;
using ServiceComponents.Core.Extensions;
using ServiceComponents.Infrastructure.Receivers;
using Xunit;

namespace ServiceComponents.UnitTests.Infrastructure
{
    public class RequestParserTests
    {
        private object _received;
        private string _message;

        private Task Parse(string payload, string typeName, bool withMessageAction = false) =>
            new RequestParser().ParseAsync(payload, typeName,
                (c, _) => { _received = c; return Task.CompletedTask; },
                (q, _) => { _received = q; return Task.CompletedTask; },
                (e, _) => { _received = e; return Task.CompletedTask; },
                withMessageAction ? (m, _) => { _message = m; return Task.CompletedTask; } : null,
                CancellationToken.None);

        private static string TypeName<T>() => $"{typeof(T).FullName}, {typeof(T).Assembly.GetName().Name}";

        [Fact]
        public async Task Type_name_selects_command_query_or_event()
        {
            await Parse("{\"Data\":\"d\",\"CommandId\":\"c1\"}", TypeName<SampleCommand>());
            Assert.Equal("c1", Assert.IsType<SampleCommand>(_received).CommandId);

            await Parse("{\"Number\":4,\"QueryId\":\"q1\"}", TypeName<SampleQuery>());
            Assert.Equal(4, Assert.IsType<SampleQuery>(_received).Number);

            await Parse("{\"Data\":\"d\",\"EventId\":\"e1\"}", TypeName<SampleEvent>());
            Assert.Equal("e1", Assert.IsType<SampleEvent>(_received).EventId);
        }

        [Fact]
        public void Type_name_format_matches_sender_side_helper()
        {
            Assert.Equal(TypeName<SampleCommand>(), new SampleCommand("x").AssemblyVersionlessQualifiedName());
        }

        [Fact]
        public async Task Missing_type_name_uses_embedded_type_property()
        {
            var payload = "{\"$type\":\"" + TypeName<SampleCommand>() + "\",\"Data\":\"d\",\"CommandId\":\"c1\"}";

            await Parse(payload, null);

            Assert.Equal("d", Assert.IsType<SampleCommand>(_received).Data);
        }

        [Fact]
        public async Task Empty_payload_with_type_name_creates_default_message()
        {
            await Parse("", TypeName<SampleCommand>());

            var command = Assert.IsType<SampleCommand>(_received);
            Assert.Null(command.Data);
            Assert.True(Guid.TryParse(command.CommandId, out _));
        }

        [Fact]
        public async Task Untyped_json_without_type_is_unknown_request()
        {
            var error = await Assert.ThrowsAsync<InvalidOperationException>(() => Parse("{\"a\":1}", null));

            Assert.Equal("Unknown request type: Newtonsoft.Json.Linq.JObject (not a command, query or event)", error.Message);
        }

        [Fact]
        public async Task Invalid_json_becomes_unknown_string_request()
        {
            var error = await Assert.ThrowsAsync<InvalidOperationException>(() => Parse("not json", TypeName<SampleCommand>()));

            Assert.Equal("Unknown request type: System.String (not a command, query or event)", error.Message);
        }

        [Fact]
        public async Task Unknown_requests_go_to_message_action_when_provided()
        {
            await Parse("raw text", null, withMessageAction: true);

            Assert.Equal("raw text", _message);
            Assert.Null(_received);
        }

        [Fact]
        public async Task Unknown_type_name_current_behavior()
        {
            // Type.GetType returns null for unknown names and Newtonsoft deserializes into a JObject.
            var error = await Assert.ThrowsAsync<InvalidOperationException>(() => Parse("{}", "Does.Not.Exist, Nowhere"));

            Assert.StartsWith("Unknown request type:", error.Message);
        }

        [Fact]
        public async Task Embedded_type_rejects_types_outside_the_allowlist()
        {
            // Changed in 0.3 (security): TypeNameHandling.All used to instantiate any type named in $type.
            var payload = "{\"$type\":\"ServiceComponents.Core.DescriptionAttribute, ServiceComponents.Core\",\"Name\":\"n\",\"Description\":\"d\"}";

            var error = await Assert.ThrowsAsync<InvalidOperationException>(() => Parse(payload, null));

            Assert.Equal("Type 'ServiceComponents.Core.DescriptionAttribute, ServiceComponents.Core' is not allowed in requests", error.Message);
            Assert.Null(_received);
        }

        [Fact]
        public async Task Embedded_type_allows_nested_types_of_message_assemblies_and_collections()
        {
            var payload = "{\"$type\":\"" + TypeName<NestedCommand>() + "\",\"Items\":{\"$type\":\"System.Collections.Generic.List`1[[" + TypeName<NestedItem>() + "]], System.Private.CoreLib\",\"$values\":[{\"$type\":\"" + TypeName<NestedItem>() + "\",\"Name\":\"a\"}]}}";

            await Parse(payload, null);

            Assert.Equal("a", Assert.IsType<NestedCommand>(_received).Items.Single().Name);
        }

        [Fact]
        public async Task Type_name_of_a_non_message_type_is_not_instantiated()
        {
            var error = await Assert.ThrowsAsync<InvalidOperationException>(() => Parse("{}", "System.Version, System.Private.CoreLib"));

            Assert.Equal("Unknown request type: System.Version (not a command, query or event)", error.Message);
        }

        public class NestedItem
        {
            public string Name { get; set; }
        }

        public class NestedCommand : Command
        {
            public System.Collections.Generic.List<NestedItem> Items { get; set; }
        }
    }
}

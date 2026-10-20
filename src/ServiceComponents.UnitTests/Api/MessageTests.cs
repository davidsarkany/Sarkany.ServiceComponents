using System;
using Newtonsoft.Json;
using ServiceComponents.Api.Mediator;
using Xunit;

namespace ServiceComponents.UnitTests.Api
{
    public class MessageTests
    {
        [Fact]
        public void Ids_are_generated_when_not_provided()
        {
            Assert.True(Guid.TryParse(new SampleCommand("x").CommandId, out _));
            Assert.True(Guid.TryParse(new OtherCommand().CommandId, out _));
            Assert.True(Guid.TryParse(new SampleQuery(1).QueryId, out _));
            Assert.True(Guid.TryParse(new SampleEvent("x").EventId, out _));
            Assert.NotEqual(new OtherCommand().CommandId, new OtherCommand().CommandId);
        }

        [Fact]
        public void Explicit_ids_are_kept()
        {
            Assert.Equal("c1", new SampleCommand("x", "c1").CommandId);
            Assert.Equal("q1", new SampleQuery(1, "q1").QueryId);
            Assert.Equal("e1", new SampleEvent("x", "e1").EventId);
        }

        [Fact]
        public void Routable_event_wraps_event_and_keeps_its_id()
        {
            var inner = new SampleEvent("x", "e1");
            var routable = new RoutableEvent(inner, "route");

            Assert.Same(inner, routable.Event);
            Assert.Equal("route", routable.RoutingKey);
            Assert.Equal("e1", routable.EventId);
        }

        [Fact]
        public void Interfaces_are_implemented()
        {
            Assert.IsAssignableFrom<ICommand>(new OtherCommand());
            Assert.IsAssignableFrom<IRequest>(new OtherCommand());
            Assert.IsAssignableFrom<IQuery<string>>(new SampleQuery(1));
            Assert.IsAssignableFrom<IQuery>(new SampleQuery(1));
            Assert.IsAssignableFrom<IRequest>(new SampleQuery(1));
            Assert.IsAssignableFrom<IEvent>(new SampleEvent("x"));
        }

        [Fact]
        public void Wire_format_uses_default_newtonsoft_pascal_case()
        {
            var json = JsonConvert.SerializeObject(new SampleCommand("payload", "c1"), Formatting.None);

            Assert.Equal("{\"Data\":\"payload\",\"CommandId\":\"c1\"}", json);
        }

        [Fact]
        public void Getter_only_ids_roundtrip_through_constructor_parameters()
        {
            var command = JsonConvert.DeserializeObject<SampleCommand>("{\"Data\":\"d\",\"CommandId\":\"c1\"}");
            var query = JsonConvert.DeserializeObject<SampleQuery>("{\"Number\":5,\"QueryId\":\"q1\"}");
            var @event = JsonConvert.DeserializeObject<SampleEvent>("{\"Data\":\"d\",\"EventId\":\"e1\"}");

            Assert.Equal(("d", "c1"), (command.Data, command.CommandId));
            Assert.Equal((5, "q1"), (query.Number, query.QueryId));
            Assert.Equal(("d", "e1"), (@event.Data, @event.EventId));
        }

        [Fact]
        public void Missing_id_in_json_generates_one()
        {
            var command = JsonConvert.DeserializeObject<SampleCommand>("{\"Data\":\"d\"}");

            Assert.True(Guid.TryParse(command.CommandId, out _));
        }
    }
}

using ServiceComponents.Api.Mediator;

namespace ServiceComponents.UnitTests
{
    public class SampleCommand : Command
    {
        public string Data { get; }

        public SampleCommand(string data, string commandId = default) : base(commandId)
        {
            Data = data;
        }
    }

    public class OtherCommand : Command
    {
        public OtherCommand() { }
    }

    public class SampleQuery : Query<string>
    {
        public int Number { get; }

        public SampleQuery(int number, string queryId = default) : base(queryId)
        {
            Number = number;
        }
    }

    public class SampleEvent : Event
    {
        public string Data { get; }

        public SampleEvent(string data, string eventId = default) : base(eventId)
        {
            Data = data;
        }
    }
}

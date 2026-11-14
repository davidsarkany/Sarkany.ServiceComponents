using System;
using System.Threading;
using System.Threading.Tasks;
using ServiceComponents.Api.Contexts;
using ServiceComponents.Api.Mediator;
using ServiceComponents.Infrastructure.Receivers;
using Xunit;

namespace ServiceComponents.UnitTests.Infrastructure
{
    [CollectionDefinition(nameof(StrictTypeAllowListTests), DisableParallelization = true)]
    public class StrictTypeAllowListCollection { }

    [Collection(nameof(StrictTypeAllowListTests))]
    public class StrictTypeAllowListTests
    {
        private static string TypeName<T>() => $"{typeof(T).FullName}, {typeof(T).Assembly.GetName().Name}";

        private static Task Parse(string payload) =>
            new RequestParser().ParseAsync(payload, null, (_, _) => Task.CompletedTask, (_, _) => Task.CompletedTask, (_, _) => Task.CompletedTask, null, CancellationToken.None);

        [Fact]
        public async Task Message_types_do_not_allow_their_assembly_when_switched_off()
        {
            // A message of ServiceComponents.Api carrying another, non-message type of the same assembly. The default
            // (the assembly of a message is allowed) is covered by RequestParserTests with the types of this assembly.
            var payload = "{\"$type\":\"" + TypeName<RoutableEvent>() + "\",\"Event\":{\"$type\":\"" + TypeName<ProcessContextAttribute>() + "\"},\"RoutingKey\":\"rk\"}";

            Assert.True(RequestParser.AllowTypesOfMessageAssemblies);
            RequestParser.AllowTypesOfMessageAssemblies = false;
            try {
                var error = await Assert.ThrowsAsync<InvalidOperationException>(() => Parse(payload));
                Assert.Equal($"Type '{TypeName<ProcessContextAttribute>()}' is not allowed in requests", error.Message);
            }
            finally {
                RequestParser.AllowTypesOfMessageAssemblies = true;
            }
        }
    }
}

using System.Reflection;
using RabbitMQ.Client;

namespace ServiceComponents.Benchmarks;

/// <summary>
/// Allocation-light <see cref="IChannel"/> stand-in: every async operation completes immediately, consumers passed to
/// BasicConsume are captured. Keeps broker I/O out of the micro benchmarks (the load tests use a real broker).
/// </summary>
public class FakeChannel : DispatchProxy
{
    private static readonly Task<string> ConsumerTag = Task.FromResult("bench-consumer");
    private static readonly Task<QueueDeclareOk> DeclareOk = Task.FromResult(new QueueDeclareOk("bench-reply", 0, 0));
    private static readonly Task<uint> Zero = Task.FromResult(0u);
    private static readonly object CompletedValueTask = default(ValueTask);

    public IAsyncBasicConsumer Consumer { get; private set; }

    public static (IChannel channel, FakeChannel fake) Create()
    {
        var channel = Create<IChannel, FakeChannel>();
        return (channel, (FakeChannel)(object)channel);
    }

    protected override object Invoke(MethodInfo targetMethod, object[] args)
    {
        if (targetMethod.Name == nameof(IChannel.BasicConsumeAsync)) {
            Consumer = args.OfType<IAsyncBasicConsumer>().Single();
            return ConsumerTag;
        }

        var returnType = targetMethod.ReturnType;
        if (returnType == typeof(ValueTask)) {
            return CompletedValueTask;
        }

        if (returnType == typeof(Task)) {
            return Task.CompletedTask;
        }

        if (returnType == typeof(Task<QueueDeclareOk>)) {
            return DeclareOk;
        }

        if (returnType == typeof(Task<uint>)) {
            return Zero;
        }

        return returnType != typeof(void) && returnType.IsValueType ? Activator.CreateInstance(returnType) : null;
    }
}

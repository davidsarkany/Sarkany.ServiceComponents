using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using FluentValidation;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using ServiceComponents.Api.Mediator;
using ServiceComponents.Core.Exceptions;

namespace ServiceComponents.Infrastructure.Rabbit;

// Query replies: the result as JSON with only CorrelationId set (wire format of the original packages), and the opt-in error replies.
internal static class RabbitReplies
{
    private const string ErrorKindHeader = "x-sc-error-kind";
    private const string ErrorMessageHeader = "x-sc-error-message";
    private const string ErrorCodeHeader = "x-sc-error-code";

    public static BasicProperties Result(string correlationId) => new() { CorrelationId = correlationId };

    public static BasicProperties Error(string correlationId, Exception exception)
    {
        var (kind, code) = exception switch {
            UnauthorizedAccessException => ("security", 0),
            NotFoundException notFound => ("notfound", notFound.ErrorCode),
            BusinessException business => ("business", business.ErrorCode),
            ValidationException => ("validation", 0),
            InvalidOperationException => ("generic", 0),
            _ => ("internal", 0)
        };

        return new BasicProperties {
            CorrelationId = correlationId,
            Headers = new Dictionary<string, object> {
                [ErrorKindHeader] = kind,
                [ErrorCodeHeader] = code,
                // Internal errors do not leak details, as on HTTP.
                [ErrorMessageHeader] = kind == "internal" ? "Something really bad happened" : exception.Message
            }
        };
    }

    /// <summary>Throws the exception described by an error reply; does nothing for result replies.</summary>
    public static void ThrowIfError(IReadOnlyBasicProperties properties)
    {
        if (properties.Headers == null || !properties.Headers.TryGetValue(ErrorKindHeader, out var kindValue)) {
            return;
        }

        var kind = Text(kindValue);
        var message = properties.Headers.TryGetValue(ErrorMessageHeader, out var messageValue) ? Text(messageValue) : null;
        var code = properties.Headers.TryGetValue(ErrorCodeHeader, out var codeValue) ? Convert.ToInt32(codeValue) : 0;

        throw kind switch {
            "security" => new UnauthorizedAccessException(message),
            "notfound" => new NotFoundException(code, message),
            "business" => new BusinessException(code, message),
            "validation" => new ValidationException(message),
            "generic" => new InvalidOperationException(message),
            _ => new Exception(message)
        };
    }

    private static string Text(object value) => value is byte[] bytes ? Encoding.UTF8.GetString(bytes) : value?.ToString();
}

// Calls IReceiveRabbitQuery.ReceiveAsync<TResult> for a query whose result type is only known at runtime.
internal abstract class RabbitQueryInvoker
{
    private static readonly ConcurrentDictionary<Type, RabbitQueryInvoker> Cache = new();

    public static RabbitQueryInvoker For(IQuery query) => Cache.GetOrAdd(query.GetType(), type => {
        var resultType = Array.Find(type.GetInterfaces(), i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IQuery<>))?.GetGenericArguments()[0]
                         ?? throw new InvalidOperationException($"{type.FullName} does not implement IQuery<TResult>");
        return (RabbitQueryInvoker)Activator.CreateInstance(typeof(RabbitQueryInvoker<>).MakeGenericType(resultType));
    });

    public abstract Task<object> ReceiveAsync(IReceiveRabbitQuery receiver, IQuery query, BasicDeliverEventArgs args, CancellationToken cancellationToken);
}

internal sealed class RabbitQueryInvoker<TResult> : RabbitQueryInvoker
{
    public override async Task<object> ReceiveAsync(IReceiveRabbitQuery receiver, IQuery query, BasicDeliverEventArgs args, CancellationToken cancellationToken) =>
        await receiver.ReceiveAsync((IQuery<TResult>)query, args, cancellationToken);
}

using Microsoft.AspNetCore.Mvc;
using ServiceComponents.AspNet.Http;

namespace Orders.Service.Controllers;

/// <summary>
/// Single endpoint receiving every command, query and event of the service.
/// The request type comes from the <c>domain-type</c> header, the body is the JSON message.
/// </summary>
[Route("api/[controller]")]
[ApiController]
public class GenericController : ControllerBase
{
    private readonly HttpRequestParser _parser;
    private readonly IReceiveHttpCommand _commandReceiver;
    private readonly IReceiveHttpQuery _queryReceiver;
    private readonly IReceiveHttpEvent _eventReceiver;

    public GenericController(HttpRequestParser parser, IReceiveHttpCommand commandReceiver, IReceiveHttpQuery queryReceiver, IReceiveHttpEvent eventReceiver)
    {
        _parser = parser;
        _commandReceiver = commandReceiver;
        _queryReceiver = queryReceiver;
        _eventReceiver = eventReceiver;
    }

    /// <summary>Posts a command, query or event.</summary>
    [HttpPost]
    public async Task<IActionResult> Post(CancellationToken cancellationToken)
    {
        object result = null;
        await _parser.Parse(Request,
            async (command, ct) => await _commandReceiver.ReceiveAsync(command, ct),
            async (query, ct) => result = await _queryReceiver.ReceiveAsync((dynamic)query, ct),
            async (@event, ct) => await _eventReceiver.ReceiveAsync(@event, ct),
            cancellationToken);
        return result == null ? Ok() : Ok(result);
    }
}

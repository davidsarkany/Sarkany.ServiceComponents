using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Serilog;

namespace ServiceComponents.AspNet.Exceptions
{
    public class ErrorHandlingMiddleware
    {
        private readonly RequestDelegate _request;
        private readonly ILogger _log;
        private readonly IExceptionMapperService _exceptionMapperService;

        public ErrorHandlingMiddleware(RequestDelegate next, ILogger log, IExceptionMapperService exceptionMapperService)
        {
            _request = next;
            _log = log;
            _exceptionMapperService = exceptionMapperService;
        }

        public async Task Invoke(HttpContext context)
        {
            try {
                await _request.Invoke(context);
            }
            catch (Exception exception) {
                if (context.Response.HasStarted) {
                    // Status and headers are already sent; the response cannot be replaced by an error response.
                    _log.Error(exception, "Request {method} {path} failed after the response started", context.Request.Method, context.Request.Path.Value);
                    throw;
                }

                await _exceptionMapperService.WriteResponse(exception, context.Response);

                // Handler failures are already logged by the log behavior; anything mapped to a server error is logged here,
                // client errors (e.g. unknown request types) at debug level.
                if (context.Response.StatusCode >= StatusCodes.Status500InternalServerError) {
                    _log.Error(exception, "Request {method} {path} failed with {statusCode}", context.Request.Method, context.Request.Path.Value, context.Response.StatusCode);
                }
                else {
                    _log.Debug(exception, "Request {method} {path} rejected with {statusCode}", context.Request.Method, context.Request.Path.Value, context.Response.StatusCode);
                }
            }
        }
    }
}

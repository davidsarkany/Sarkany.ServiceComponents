using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Mime;
using System.Threading.Tasks;
using FluentValidation;
using FluentValidation.Results;
using Microsoft.AspNetCore.Http;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using ServiceComponents.Core.Exceptions;

namespace ServiceComponents.AspNet.Exceptions
{
    public class JsonExceptionMapper : IExceptionMapperService
    {
        private readonly Func<Exception, (int statusCode, IErrorResponse response)> _responseMapper;
        private readonly Func<HttpStatusCode, HttpContent, Exception> _exceptionMapper;

        /// <summary>
        /// Exception mapper with built-in mapping
        /// </summary>
        public JsonExceptionMapper() : this (exception => {

            return exception switch {
                UnauthorizedAccessException unauthorizedAccessException => ((int)HttpStatusCode.Unauthorized, new SecurityErrorResponse(unauthorizedAccessException.Message)),
                NotImplementedException notImplementedException => ((int)HttpStatusCode.NotImplemented, new GenericErrorResponse(notImplementedException.Message)),
                NotFoundException notFoundException => new((int)HttpStatusCode.NotFound, new GenericErrorResponse(notFoundException.Message)),
                BusinessException businessException => new((int)HttpStatusCode.BadRequest, new BusinessErrorResponse(businessException.ErrorCode, businessException.Message)),
                ValidationException validationException => new((int)HttpStatusCode.BadRequest, new ValidationErrorResponse("Validation failed", validationException.Errors.Select(failure => new ValidationErrorResponseItem(failure.PropertyName, failure.ErrorCode, failure.ErrorMessage, failure.AttemptedValue)))),
                InvalidOperationException invalidOperationException => new((int)HttpStatusCode.BadRequest, new GenericErrorResponse(exception.Message)),
                _ => new((int)HttpStatusCode.InternalServerError, new GenericErrorResponse("Something really bad happened"))
            };

        }, (statusCode, httpContent) => {

            // The content is buffered by ThrowExceptionIfNeeded, so reading it here does not block.
            var body = httpContent?.ReadAsStringAsync().GetAwaiter().GetResult();
            var errorResponse = ParseErrorResponse(body);

            return (statusCode, errorResponse) switch {
                (HttpStatusCode.Unauthorized, {} response) => new UnauthorizedAccessException(response.ErrorMessage),
                (HttpStatusCode.NotImplemented, {} response) => new NotImplementedException(response.ErrorMessage),
                (HttpStatusCode.NotFound, { } response) => new NotFoundException(response.ErrorMessage),
                (HttpStatusCode.BadRequest, BusinessErrorResponse response) => new BusinessException(response.ErrorCode, response.ErrorMessage),
                (HttpStatusCode.BadRequest, ValidationErrorResponse response) => new ValidationException(response.ErrorMessage, response.Errors.Select(error => new ValidationFailure(error.PropertyName, error.ErrorMessage, error.AttemptedValue) { ErrorCode = error.ErrorCode }), false),
                (HttpStatusCode.BadRequest, { } response) => new InvalidOperationException(response.ErrorMessage),
                // Error bodies not produced by this mapper (e.g. an empty 401 of the JWT middleware) map by status code.
                (HttpStatusCode.Unauthorized, null) => new UnauthorizedAccessException("Unauthorized"),
                (HttpStatusCode.NotImplemented, null) => new NotImplementedException(),
                (HttpStatusCode.NotFound, null) => new NotFoundException("Not found"),
                (HttpStatusCode.BadRequest, null) => new InvalidOperationException(string.IsNullOrWhiteSpace(body) ? "Bad request" : body),
                (_, _) => new Exception("Unknown error")
            };
        })
        {
        }

        /// <summary>
        /// Exception mapper with custom mapping
        /// </summary>
        /// <param name="responseMapper"></param>
        /// <param name="exceptionMapper"></param>
        public JsonExceptionMapper(Func<Exception, (int statusCode, IErrorResponse response)> responseMapper, Func<HttpStatusCode, HttpContent, Exception> exceptionMapper)
        {
            _responseMapper = responseMapper;
            _exceptionMapper = exceptionMapper;
        }

        public async Task WriteResponse(Exception exception, HttpResponse httpResponse)
        {
            var (statusCode, response) = _responseMapper(exception);

            httpResponse.StatusCode = statusCode;
            httpResponse.ContentType = MediaTypeNames.Application.Json;
            await httpResponse.WriteAsync(JsonConvert.SerializeObject(response));
        }

        public async Task ThrowExceptionIfNeeded(HttpResponseMessage httpResponse)
        {
            if (httpResponse.IsSuccessStatusCode) {
                return;
            }

            if (httpResponse.Content != null) {
                await httpResponse.Content.LoadIntoBufferAsync();
            }

            throw _exceptionMapper(httpResponse.StatusCode, httpResponse.Content) ?? new Exception("Unknown error");

        }

        private static IErrorResponse ParseErrorResponse(string body)
        {
            if (string.IsNullOrWhiteSpace(body)) {
                return null;
            }

            JObject jsonObject;
            try {
                jsonObject = JObject.Parse(body);
            }
            catch (JsonReaderException) {
                return null;
            }

            return jsonObject.GetValue("type", StringComparison.OrdinalIgnoreCase)?.Value<string>() switch {
                "business" => jsonObject.ToObject<BusinessErrorResponse>(),
                "validation" => jsonObject.ToObject<ValidationErrorResponse>(),
                "security" => jsonObject.ToObject<SecurityErrorResponse>(),
                "generic" => jsonObject.ToObject<GenericErrorResponse>(),
                _ => null
            };
        }
    }
}

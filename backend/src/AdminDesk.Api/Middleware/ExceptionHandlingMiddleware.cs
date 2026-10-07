using System.Text.Json;
using AdminDesk.Application.Abstractions;
using AdminDesk.SharedKernel.Constants;
using AdminDesk.SharedKernel.Exceptions;
using AdminDesk.SharedKernel.Responses;
using Microsoft.Extensions.Options;
using Microsoft.AspNetCore.Mvc;

namespace AdminDesk.Api.Middleware;

public class ExceptionHandlingMiddleware
{
    public const string GenericMessage = "Something went wrong on our side.";

    private readonly RequestDelegate _next;
    private readonly ILogger<ExceptionHandlingMiddleware> _logger;
    private readonly JsonSerializerOptions _json;

    public ExceptionHandlingMiddleware(
        RequestDelegate next,
        ILogger<ExceptionHandlingMiddleware> logger,
        IOptions<Microsoft.AspNetCore.Http.Json.JsonOptions> jsonOptions)
    {
        _next = next;
        _logger = logger;
        _json = jsonOptions.Value.SerializerOptions;
    }

    public async Task InvokeAsync(HttpContext context, ICorrelationIdAccessor correlation)
    {
        try
        {
            await _next(context);
        }
        catch (Exception ex)
        {
            if (context.Response.HasStarted)
            {
                _logger.LogError(ex, "Unhandled error after the response had started");
                throw;
            }

            var correlationId = correlation.CorrelationId;
            ApiResponse body;
            int status;

            switch (ex)
            {
                case DefinitionLoadException dle:
                    _logger.LogError(dle, "Definition problem: {Message}", dle.Message);
                    status = dle.HttpStatus;
                    body = ApiResponse.Fail(dle.Code, GenericMessage, null, correlationId);
                    break;
                case ValidationException ve:
                    status = ve.HttpStatus;
                    body = ApiResponse.Fail(ve.Code, ve.Message, ve.FieldErrors, correlationId);
                    break;
                case AppException ae:
                    status = ae.HttpStatus;
                    body = ApiResponse.Fail(ae.Code, ae.Message, null, correlationId);
                    break;
                case BadHttpRequestException bad:
                    status = StatusCodes.Status400BadRequest;
                    body = ApiResponse.Fail(ErrorCodes.VALIDATION_FAILED, "The request could not be read.", null, correlationId);
                    _logger.LogDebug(bad, "Bad request");
                    break;
                default:
                    _logger.LogError(ex, "Unhandled error on {Method} {Path}", context.Request.Method, context.Request.Path);
                    status = StatusCodes.Status500InternalServerError;
                    body = ApiResponse.Fail(ErrorCodes.INTERNAL_ERROR, GenericMessage, null, correlationId);
                    break;
            }

            context.Response.Clear();
            context.Response.StatusCode = status;
            context.Response.ContentType = "application/json";
            await JsonSerializer.SerializeAsync(context.Response.Body, body, body.GetType(), _json);
        }
    }
}

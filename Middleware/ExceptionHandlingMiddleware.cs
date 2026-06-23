using FloraCore.Application.Common.Models;
using FloraCore.Domain.Exceptions;
using FluentValidation;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Diagnostics;

namespace FloraCore.Middleware;

/// <summary>
/// Middleware for handling exceptions and returning standardized API responses.
/// </summary>
public class ExceptionHandlingMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<ExceptionHandlingMiddleware> _logger;
    private readonly IWebHostEnvironment _env;

    /// <summary>
    /// Initializes a new instance of the <see cref="ExceptionHandlingMiddleware"/> class.
    /// </summary>
    /// <param name="next">The next request delegate.</param>
    /// <param name="logger">The logger.</param>
    /// <param name="env">The web host environment.</param>
    public ExceptionHandlingMiddleware(RequestDelegate next, ILogger<ExceptionHandlingMiddleware> logger, IWebHostEnvironment env)
    {
        _next = next ?? throw new ArgumentNullException(nameof(next));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _env = env ?? throw new ArgumentNullException(nameof(env));
    }

    /// <summary>
    /// Invokes the middleware.
    /// </summary>
    /// <param name="context">The HTTP context.</param>
    public async Task InvokeAsync(HttpContext context)
    {
        if (context.Request.Headers.TryGetValue("X-Trace-Id", out var traceIdHeader) && !string.IsNullOrEmpty(traceIdHeader))
        {
            context.TraceIdentifier = traceIdHeader.ToString();
        }

        using (Serilog.Context.LogContext.PushProperty("TraceId", context.TraceIdentifier))
        {
            try
            {
                await _next(context);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "An unhandled exception has occurred.");
                await HandleExceptionAsync(context, ex);
            }
        }
    }

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private async Task HandleExceptionAsync(HttpContext context, Exception exception)
    {
        context.Response.ContentType = "application/problem+json";
        ProblemDetails problemDetails;
        int statusCode;

        switch (exception)
        {
            case ValidationException validationException:
                statusCode = StatusCodes.Status400BadRequest;
                problemDetails = new ValidationProblemDetails(
                    validationException.Errors.GroupBy(e => e.PropertyName)
                        .ToDictionary(g => g.Key, g => g.Select(e => e.ErrorMessage).ToArray()))
                {
                    Title = "Validation Failed",
                    Detail = "One or more validation errors occurred.",
                    Status = statusCode,
                    Instance = context.Request.Path
                };
                break;

            case EntityNotFoundException:
            case KeyNotFoundException:
            case FileNotFoundException:
                statusCode = StatusCodes.Status404NotFound;
                problemDetails = new ProblemDetails
                {
                    Title = "Resource Not Found",
                    Detail = exception.Message,
                    Status = statusCode,
                    Instance = context.Request.Path
                };
                break;

            case UnauthorizedAccessException:
                statusCode = StatusCodes.Status401Unauthorized;
                problemDetails = new ProblemDetails
                {
                    Title = "Unauthorized",
                    Detail = "Unauthorized access.",
                    Status = statusCode,
                    Instance = context.Request.Path
                };
                break;

            case AccessDeniedException:
                statusCode = StatusCodes.Status403Forbidden;
                problemDetails = new ProblemDetails
                {
                    Title = "Forbidden",
                    Detail = exception.Message,
                    Status = statusCode,
                    Instance = context.Request.Path
                };
                break;

            case DomainException:
            case ArgumentException:
                statusCode = StatusCodes.Status400BadRequest;
                problemDetails = new ProblemDetails
                {
                    Title = "Bad Request",
                    Detail = exception.Message,
                    Status = statusCode,
                    Instance = context.Request.Path
                };
                break;

            default:
                statusCode = StatusCodes.Status500InternalServerError;
                var message = (_env.IsEnvironment("Testing") || _env.IsDevelopment())
                    ? exception.Message
                    : "An internal server error occurred.";

                problemDetails = new ProblemDetails
                {
                    Title = "Internal Server Error",
                    Detail = message,
                    Status = statusCode,
                    Instance = context.Request.Path
                };

                if (_env.IsDevelopment())
                {
                    problemDetails.Extensions["stackTrace"] = exception.StackTrace;
                }
                break;
        }

        problemDetails.Extensions["traceId"] = context.TraceIdentifier;

        context.Response.StatusCode = statusCode;
        await context.Response.WriteAsync(JsonSerializer.Serialize(problemDetails, SerializerOptions));
    }
}

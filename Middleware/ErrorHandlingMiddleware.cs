using System.Net;
using System.Text.Json;
using Stripe;

namespace StripeTerminalBackend.Middleware;

public class ErrorHandlingMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<ErrorHandlingMiddleware> _logger;

    public ErrorHandlingMiddleware(
        RequestDelegate next,
        ILogger<ErrorHandlingMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await _next(context);
        }
        catch (StripeException ex)
        {
            _logger.LogError(ex, "Stripe error on {Method} {Path}",
                context.Request.Method, context.Request.Path);

            await WriteError(context, HttpStatusCode.BadGateway,
                "stripe_error", ex.Message);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unhandled exception on {Method} {Path}",
                context.Request.Method, context.Request.Path);

            await WriteError(context, HttpStatusCode.InternalServerError,
                "internal_error", "An unexpected error occurred.");
        }
    }

    private static Task WriteError(
        HttpContext context,
        HttpStatusCode status,
        string code,
        string message)
    {
        context.Response.ContentType = "application/json";
        context.Response.StatusCode = (int)status;

        var body = JsonSerializer.Serialize(new { code, error = message });
        return context.Response.WriteAsync(body);
    }
}
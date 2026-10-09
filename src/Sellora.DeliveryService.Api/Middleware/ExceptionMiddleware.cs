using Sellora.DeliveryService.Infrastructure.Dependencies;

namespace Sellora.DeliveryService.Api.Middleware;

/// <summary>
/// Translates well-known infrastructure exceptions into RFC-7807 problem responses
/// so that unhandled dependency failures never surface as opaque 500s.
/// </summary>
public sealed class ExceptionMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<ExceptionMiddleware> _logger;

    public ExceptionMiddleware(RequestDelegate next, ILogger<ExceptionMiddleware> logger)
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
        catch (DependencyUnavailableException ex)
        {
            _logger.LogError(ex, "Dependency unavailable: {Dependency}", ex.Message);

            if (context.Response.HasStarted) return;

            context.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
            context.Response.ContentType = "application/problem+json";
            await context.Response.WriteAsJsonAsync(new
            {
                type = "https://sellora.io/errors/dependency-unavailable",
                title = "A downstream service is temporarily unavailable.",
                status = 503,
                detail = ex.Message
            }, cancellationToken: context.RequestAborted);
        }
        catch (DependencyRejectedException ex)
        {
            _logger.LogWarning(ex, "Dependency rejected request: {Dependency} status={Status}", ex.Message, ex.Status);

            if (context.Response.HasStarted) return;

            context.Response.StatusCode = StatusCodes.Status502BadGateway;
            context.Response.ContentType = "application/problem+json";
            await context.Response.WriteAsJsonAsync(new
            {
                type = "https://sellora.io/errors/dependency-rejected",
                title = "A downstream service rejected the request.",
                status = 502,
                detail = ex.Message
            }, cancellationToken: context.RequestAborted);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Unhandled exception");

            if (context.Response.HasStarted) return;

            context.Response.StatusCode = StatusCodes.Status500InternalServerError;
            context.Response.ContentType = "application/problem+json";
            await context.Response.WriteAsJsonAsync(new
            {
                type = "https://sellora.io/errors/internal",
                title = "An unexpected error occurred.",
                status = 500
            }, cancellationToken: context.RequestAborted);
        }
    }
}

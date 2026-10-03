namespace Sellora.DeliveryService.Api.Middleware;

public class CorrelationIdMiddleware(RequestDelegate next)
{
    private const string HeaderName = "x-correlation-id";

    public async Task InvokeAsync(HttpContext context)
    {
        if (!context.Request.Headers.TryGetValue(HeaderName, out var correlationId))
            correlationId = Guid.NewGuid().ToString();

        context.Response.Headers[HeaderName] = correlationId.ToString();
        context.Items[HeaderName] = correlationId.ToString();

        using var scope = Serilog.Context.LogContext.PushProperty("CorrelationId", correlationId.ToString());
        await next(context);
    }
}

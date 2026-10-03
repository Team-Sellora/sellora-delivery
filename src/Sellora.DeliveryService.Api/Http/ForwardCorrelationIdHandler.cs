namespace Sellora.DeliveryService.Api.Http;

/// <summary>
/// Forwards x-correlation-id from the current request to any downstream HTTP calls.
/// </summary>
internal class ForwardCorrelationIdHandler(IHttpContextAccessor httpContextAccessor)
    : DelegatingHandler
{
    private const string HeaderName = "x-correlation-id";

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        if (httpContextAccessor.HttpContext?.Items.TryGetValue(HeaderName, out var correlationId) == true
            && correlationId is string cid)
        {
            request.Headers.TryAddWithoutValidation(HeaderName, cid);
        }

        return base.SendAsync(request, cancellationToken);
    }
}

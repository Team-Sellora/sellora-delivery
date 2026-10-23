namespace Sellora.DeliveryService.Api.Http;

/// <summary>
/// Forwards the Bearer token from the incoming request to any downstream HTTP calls.
/// </summary>
internal class ForwardBearerTokenHandler(IHttpContextAccessor httpContextAccessor)
    : DelegatingHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var token = httpContextAccessor.HttpContext?.Request.Headers.Authorization.FirstOrDefault();
        if (!string.IsNullOrWhiteSpace(token))
            request.Headers.TryAddWithoutValidation("Authorization", token);

        return base.SendAsync(request, cancellationToken);
    }
}

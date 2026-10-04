using System.Net.Http.Json;
using System.Text.Json;
using Polly.CircuitBreaker;
using Polly.Timeout;
using Sellora.DeliveryService.Application.Dependencies;

namespace Sellora.DeliveryService.Infrastructure.Dependencies;

internal static class DependencyHttp
{
    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public static async Task<HttpResponseMessage> SendAsync(
        HttpClient http,
        HttpRequestMessage request,
        Dependency dependency,
        CancellationToken cancellationToken)
    {
        HttpResponseMessage response;

        try
        {
            response = await http.SendAsync(request, cancellationToken);
        }
        catch (Exception exception) when (
            exception is TimeoutRejectedException
                or BrokenCircuitException
                or HttpRequestException
            || (exception is TaskCanceledException && !cancellationToken.IsCancellationRequested))
        {
            throw new DependencyUnavailableException(dependency, exception);
        }

        if ((int)response.StatusCode >= 500)
        {
            response.Dispose();
            throw new DependencyUnavailableException(dependency);
        }

        return response;
    }

    public static async Task<T> ReadAsync<T>(
        HttpResponseMessage response,
        Dependency dependency,
        CancellationToken cancellationToken)
    {
        var body = await response.Content.ReadFromJsonAsync<T>(Json, cancellationToken);
        return body ?? throw new DependencyRejectedException(
            dependency, response.StatusCode, "The response body was empty.");
    }

    public static async Task<string?> ReadErrorAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        try
        {
            var body = await response.Content.ReadFromJsonAsync<ErrorBody>(Json, cancellationToken);
            return body?.Detail ?? body?.Message ?? body?.Title;
        }
        catch (JsonException)
        {
            return null;
        }
        catch (NotSupportedException)
        {
            return null;
        }
    }

    private sealed record ErrorBody(string? Message, string? Title, string? Detail);
}

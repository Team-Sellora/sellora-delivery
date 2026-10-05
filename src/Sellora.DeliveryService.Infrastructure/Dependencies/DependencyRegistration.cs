using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http.Resilience;
using Polly;
using Sellora.DeliveryService.Application.Dependencies;

namespace Sellora.DeliveryService.Infrastructure.Dependencies;

public static class DependencyRegistration
{
    public static IHttpClientBuilder AddDeliveryDependencies(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var options = configuration.GetSection(DependencyOptions.Section).Get<DependencyOptions>()
            ?? new DependencyOptions();

        var builder = services.AddHttpClient<IOrganizationClient, OrganizationClient>(client =>
                client.BaseAddress = BaseAddress(options.Organization, "Organization"))
            .AddResilience("organization", options.Organization, options.CircuitBreaker);

        return builder;
    }

    private static IHttpClientBuilder AddResilience(
        this IHttpClientBuilder builder,
        string name,
        EndpointOptions endpoint,
        CircuitBreakerSettings breaker)
    {
        builder.AddResilienceHandler($"{name}-pipeline", pipeline =>
        {
            pipeline.AddCircuitBreaker(new HttpCircuitBreakerStrategyOptions
            {
                FailureRatio = breaker.FailureRatio,
                MinimumThroughput = breaker.MinimumThroughput,
                SamplingDuration = TimeSpan.FromSeconds(Math.Max(
                    breaker.SamplingDurationSeconds, endpoint.TimeoutSeconds * 2)),
                BreakDuration = TimeSpan.FromSeconds(breaker.BreakDurationSeconds)
            });

            pipeline.AddTimeout(TimeSpan.FromSeconds(endpoint.TimeoutSeconds));
        });

        return builder;
    }

    private static Uri BaseAddress(EndpointOptions endpoint, string name)
    {
        if (!Uri.TryCreate(endpoint.BaseUrl, UriKind.Absolute, out var uri))
        {
            throw new InvalidOperationException(
                $"Dependencies:{name}:BaseUrl must be an absolute URL (got '{endpoint.BaseUrl}').");
        }

        return uri.AbsoluteUri.EndsWith('/') ? uri : new Uri(uri.AbsoluteUri + "/");
    }
}

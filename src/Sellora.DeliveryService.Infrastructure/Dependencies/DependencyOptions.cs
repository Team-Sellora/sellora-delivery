namespace Sellora.DeliveryService.Infrastructure.Dependencies;

public sealed class DependencyOptions
{
    public const string Section = "Dependencies";

    public EndpointOptions Organization { get; set; } = new();

    public CircuitBreakerSettings CircuitBreaker { get; set; } = new();
}

public sealed class EndpointOptions
{
    public string BaseUrl { get; set; } = string.Empty;

    public double TimeoutSeconds { get; set; } = 2;
}

public sealed class CircuitBreakerSettings
{
    public double FailureRatio { get; set; } = 0.5;

    public int MinimumThroughput { get; set; } = 5;

    public double SamplingDurationSeconds { get; set; } = 30;

    public double BreakDurationSeconds { get; set; } = 30;
}

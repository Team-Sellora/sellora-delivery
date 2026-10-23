using Sellora.DeliveryService.Application.Dependencies;

namespace Sellora.DeliveryService.Infrastructure.Dependencies;

public sealed class DependencyUnavailableException : Exception
{
    public DependencyUnavailableException(Dependency dependency, Exception? innerException = null)
        : base($"{dependency} is unavailable.", innerException)
    {
        Dependency = dependency;
    }

    public Dependency Dependency { get; }
}

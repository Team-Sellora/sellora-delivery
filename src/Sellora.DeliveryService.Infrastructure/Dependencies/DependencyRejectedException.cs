using System.Net;
using Sellora.DeliveryService.Application.Dependencies;

namespace Sellora.DeliveryService.Infrastructure.Dependencies;

public sealed class DependencyRejectedException : Exception
{
    public DependencyRejectedException(Dependency dependency, HttpStatusCode status, string? detail)
        : base($"{dependency} rejected the request (HTTP {(int)status}){(string.IsNullOrWhiteSpace(detail) ? "." : $": {detail}")}")
    {
        Dependency = dependency;
        Status = status;
    }

    public Dependency Dependency { get; }

    public HttpStatusCode Status { get; }
}

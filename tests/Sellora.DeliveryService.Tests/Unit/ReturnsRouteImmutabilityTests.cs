using System.Reflection;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;
using Sellora.DeliveryService.Api.Controllers;
using Xunit;

namespace Sellora.DeliveryService.Tests.Unit;

/// <summary>
/// Immutability is enforced by the absence of edit routes on return lines.
/// Once a return request is submitted, its lines cannot be edited by any caller.
///
/// PUT /api/returns/{id}/decision is the only allowed PUT route, which only sets
/// the agency operator's decision and reason, and never modifies lines.
/// </summary>
public sealed class ReturnsRouteImmutabilityTests
{
    private static readonly string[] AllowedDecisionBodyProperties = ["Decision", "Reason"];

    private static readonly string[] EditVerbs = ["PUT", "PATCH"];

    private static readonly string[] AllowedPutRoutes =
    [
        "api/returns/{id:guid}/decision",
        "api/returns/{id}/decision"
    ];

    [Fact]
    public void No_controller_in_the_api_maps_put_or_patch_onto_return_lines()
    {
        var controllers = typeof(DeliveriesController).Assembly.GetTypes()
            .Where(type => typeof(ControllerBase).IsAssignableFrom(type) && !type.IsAbstract);

        var lineOffenders = new List<string>();

        foreach (var controller in controllers)
        {
            var classRoute = controller.GetCustomAttribute<RouteAttribute>()?.Template ?? string.Empty;

            foreach (var method in controller.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly))
            {
                foreach (var attribute in method.GetCustomAttributes<HttpMethodAttribute>())
                {
                    var fullRoute = $"{classRoute}/{attribute.Template}".Trim('/').ToLowerInvariant();

                    if (fullRoute.Contains("returns") && fullRoute.Contains("line") &&
                        attribute.HttpMethods.Any(verb => EditVerbs.Contains(verb, StringComparer.OrdinalIgnoreCase)))
                    {
                        lineOffenders.Add($"{controller.Name}.{method.Name} -> {fullRoute}");
                    }
                }
            }
        }

        lineOffenders.Should().BeEmpty();
    }

    [Fact]
    public void Any_put_on_returns_is_strictly_the_decision_route_and_never_touches_lines()
    {
        var controllers = typeof(DeliveriesController).Assembly.GetTypes()
            .Where(type => typeof(ControllerBase).IsAssignableFrom(type) && !type.IsAbstract);

        var putRoutesOnReturns = new List<string>();

        foreach (var controller in controllers)
        {
            var classRoute = controller.GetCustomAttribute<RouteAttribute>()?.Template ?? string.Empty;

            foreach (var method in controller.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly))
            {
                foreach (var attribute in method.GetCustomAttributes<HttpMethodAttribute>())
                {
                    if (attribute.HttpMethods.Any(verb => string.Equals(verb, "PUT", StringComparison.OrdinalIgnoreCase)))
                    {
                        var fullRoute = $"{classRoute}/{attribute.Template}".Trim('/').ToLowerInvariant();
                        if (fullRoute.StartsWith("api/returns"))
                        {
                            putRoutesOnReturns.Add(fullRoute);
                        }
                    }
                }
            }
        }

        // Any PUT route mapped under api/returns must match the allowed decision route
        foreach (var route in putRoutesOnReturns)
        {
            AllowedPutRoutes.Select(r => r.ToLowerInvariant()).Should().Contain(route);
        }
    }

    [Fact]
    public void The_only_put_on_returns_is_the_decision_and_its_body_only_carries_decision_and_reason()
    {
        var bodyProperties = typeof(Sellora.DeliveryService.Api.Contracts.DecideReturnRequest)
            .GetProperties()
            .Select(property => property.Name)
            .OrderBy(name => name);

        bodyProperties.Should().BeEquivalentTo(AllowedDecisionBodyProperties);
    }
}

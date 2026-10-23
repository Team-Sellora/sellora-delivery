using System.Security.Claims;
using FluentAssertions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Infrastructure;
using Sellora.DeliveryService.Api.Security;
using Xunit;

namespace Sellora.DeliveryService.Tests.Unit;

public class RolePoliciesTests
{
    [Fact]
    public async Task RolePolicies_Register_ShouldAddAndEnforceAllPolicies()
    {
        var options = new AuthorizationOptions();
        RolePolicies.Register(options);

        var agencyReadPolicy = options.GetPolicy(RolePolicies.AgencyRead);
        agencyReadPolicy.Should().NotBeNull();
        options.GetPolicy(RolePolicies.AgencyWrite).Should().NotBeNull();
        options.GetPolicy(RolePolicies.RepRead).Should().NotBeNull();
        options.GetPolicy(RolePolicies.RepWrite).Should().NotBeNull();
        options.GetPolicy(RolePolicies.ShopOwnerWrite).Should().NotBeNull();
        options.GetPolicy(RolePolicies.SystemAdmin).Should().NotBeNull();

        // Test AssertionRequirement directly
        var req = agencyReadPolicy!.Requirements.OfType<AssertionRequirement>().First();

        var agencyUser = CreateUser("roles", SelloraRoles.AgencyOperator);
        var ctx1 = new AuthorizationHandlerContext(agencyReadPolicy.Requirements, agencyUser, null);
        await req.HandleAsync(ctx1);
        ctx1.HasSucceeded.Should().BeTrue();

        var repUser = CreateUser("roles", SelloraRoles.SalesRep);
        var ctx2 = new AuthorizationHandlerContext(agencyReadPolicy.Requirements, repUser, null);
        await req.HandleAsync(ctx2);
        ctx2.HasSucceeded.Should().BeFalse();

        // Namespaced role e.g. "tenant/AgencyOperator"
        var namespacedUser = CreateUser("roles", "tenant/" + SelloraRoles.AgencyOperator);
        var ctx3 = new AuthorizationHandlerContext(agencyReadPolicy.Requirements, namespacedUser, null);
        await req.HandleAsync(ctx3);
        ctx3.HasSucceeded.Should().BeTrue();

        // Also test with standard ClaimTypes.Role
        var roleClaimUser = CreateUser(ClaimTypes.Role, SelloraRoles.SystemAdmin);
        var sysAdminPolicy = options.GetPolicy(RolePolicies.SystemAdmin)!;
        var sysAdminReq = sysAdminPolicy.Requirements.OfType<AssertionRequirement>().First();
        var ctx4 = new AuthorizationHandlerContext(sysAdminPolicy.Requirements, roleClaimUser, null);
        await sysAdminReq.HandleAsync(ctx4);
        ctx4.HasSucceeded.Should().BeTrue();
    }

    private static ClaimsPrincipal CreateUser(string claimType, string roleValue)
    {
        var identity = new ClaimsIdentity([new Claim(claimType, roleValue)], "TestAuth");
        return new ClaimsPrincipal(identity);
    }
}

namespace Sellora.DeliveryService.Api.Security;

public static class RolePolicies
{
    public const string AgencyRead = "AgencyRead";
    public const string AgencyWrite = "AgencyWrite";
    public const string RepRead = "RepRead";
    public const string RepWrite = "RepWrite";
    public const string ShopOwnerWrite = "ShopOwnerWrite";
    public const string SystemAdmin = "SystemAdmin";

    public static void Register(Microsoft.AspNetCore.Authorization.AuthorizationOptions opts)
    {
        opts.AddPolicy(AgencyRead, p => p.RequireAssertion(HasAnyRole(SelloraRoles.AgencyOperator, SelloraRoles.SystemAdmin)));
        opts.AddPolicy(AgencyWrite, p => p.RequireAssertion(HasAnyRole(SelloraRoles.AgencyOperator, SelloraRoles.SystemAdmin)));
        opts.AddPolicy(RepRead, p => p.RequireAssertion(HasAnyRole(SelloraRoles.SalesRep, SelloraRoles.AgencyOperator, SelloraRoles.SystemAdmin)));
        opts.AddPolicy(RepWrite, p => p.RequireAssertion(HasAnyRole(SelloraRoles.SalesRep, SelloraRoles.SystemAdmin)));
        opts.AddPolicy(ShopOwnerWrite, p => p.RequireAssertion(HasAnyRole(SelloraRoles.ShopOwner, SelloraRoles.SystemAdmin)));
        opts.AddPolicy(SystemAdmin, p => p.RequireAssertion(HasAnyRole(SelloraRoles.SystemAdmin)));
    }

    private static Func<Microsoft.AspNetCore.Authorization.AuthorizationHandlerContext, bool> HasAnyRole(params string[] roles) =>
        ctx => ctx.User.Claims
            .Where(c => c.Type == "roles" || c.Type == "http://schemas.microsoft.com/ws/2008/06/identity/claims/role")
            .Select(c => c.Value.Contains('/') ? c.Value.Split('/')[1] : c.Value)
            .Any(r => roles.Contains(r));
}

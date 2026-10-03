namespace Sellora.DeliveryService.Api.Security;

public static class RolePolicies
{
    public const string AgencyRead = "AgencyRead";
    public const string AgencyWrite = "AgencyWrite";
    public const string RepRead = "RepRead";
    public const string RepWrite = "RepWrite";
    public const string SystemAdmin = "SystemAdmin";

    public static void Register(Microsoft.AspNetCore.Authorization.AuthorizationOptions opts)
    {
        opts.AddPolicy(AgencyRead, p => p.RequireRole(SelloraRoles.AgencyOperator, SelloraRoles.SystemAdmin));
        opts.AddPolicy(AgencyWrite, p => p.RequireRole(SelloraRoles.AgencyOperator, SelloraRoles.SystemAdmin));
        opts.AddPolicy(RepRead, p => p.RequireRole(SelloraRoles.SalesRep, SelloraRoles.AgencyOperator, SelloraRoles.SystemAdmin));
        opts.AddPolicy(RepWrite, p => p.RequireRole(SelloraRoles.SalesRep, SelloraRoles.SystemAdmin));
        opts.AddPolicy(SystemAdmin, p => p.RequireRole(SelloraRoles.SystemAdmin));
    }
}

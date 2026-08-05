//--------------------------//
//--------把强制改密状态加入认证主体供授权策略检查---------//
//--------Adds forced-password-change state to the principal for authorization policies--------//
//-------------------------//
using System.Security.Claims;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Nas.Application.Authentication;
using Nas.Infrastructure.Persistence.Cluster;

namespace Nas.Infrastructure.Authentication;

public sealed class NasUserClaimsPrincipalFactory(
    UserManager<NasUser> userManager,
    RoleManager<NasRole> roleManager,
    ClusterDbContext database,
    IOptions<IdentityOptions> options)
    : UserClaimsPrincipalFactory<NasUser, NasRole>(userManager, roleManager, options)
{
    protected override async Task<ClaimsIdentity> GenerateClaimsAsync(NasUser user)
    {
        var identity = await base.GenerateClaimsAsync(user);
        identity.AddClaim(new Claim(
            AuthenticationDefaults.MustChangePasswordClaim,
            user.MustChangePassword.ToString().ToLowerInvariant()));

        var roleNames = await UserManager.GetRolesAsync(user);
        var normalizedRoleNames = roleNames
            .Select(RoleManager.NormalizeKey)
            .Where(name => name is not null)
            .ToArray();
        var roleIds = await RoleManager.Roles
            .Where(role => normalizedRoleNames.Contains(role.NormalizedName))
            .Select(role => role.Id)
            .ToArrayAsync();
        var permissions = await database.RolePermissions
            .Where(permission => roleIds.Contains(permission.RoleId))
            .Select(permission => permission.PermissionCode)
            .Distinct()
            .ToArrayAsync();
        identity.AddClaims(permissions.Select(permission => new Claim(
            AuthenticationDefaults.PermissionClaim,
            permission)));
        return identity;
    }
}

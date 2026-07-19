//--------------------------//
//--------把强制改密状态加入认证主体供授权策略检查---------//
//--------Adds forced-password-change state to the principal for authorization policies--------//
//-------------------------//
using System.Security.Claims;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;
using Nas.Application.Authentication;
using Nas.Infrastructure.Persistence.Cluster;

namespace Nas.Infrastructure.Authentication;

public sealed class NasUserClaimsPrincipalFactory(
    UserManager<NasUser> userManager,
    RoleManager<NasRole> roleManager,
    IOptions<IdentityOptions> options)
    : UserClaimsPrincipalFactory<NasUser, NasRole>(userManager, roleManager, options)
{
    protected override async Task<ClaimsIdentity> GenerateClaimsAsync(NasUser user)
    {
        var identity = await base.GenerateClaimsAsync(user);
        identity.AddClaim(new Claim(
            AuthenticationDefaults.MustChangePasswordClaim,
            user.MustChangePassword.ToString().ToLowerInvariant()));
        return identity;
    }
}

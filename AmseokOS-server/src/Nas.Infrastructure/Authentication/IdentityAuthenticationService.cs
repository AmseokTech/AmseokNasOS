//--------------------------//
//--------使用 ASP.NET Core Identity 编排登录、改密与会话撤销---------//
//--------Uses ASP.NET Core Identity to coordinate sign-in, password changes, and session revocation--------//
//-------------------------//
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Nas.Application.Authentication;
using Nas.Infrastructure.Persistence.Cluster;

namespace Nas.Infrastructure.Authentication;

public sealed class IdentityAuthenticationService(
    UserManager<NasUser> userManager,
    SignInManager<NasUser> signInManager,
    ClusterDbContext database,
    ILogger<IdentityAuthenticationService> logger) : IAuthenticationService
{
    public async Task<SignInOutcome> SignInAdministratorAsync(
        string password,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var result = await signInManager.PasswordSignInAsync(
            AuthenticationDefaults.AdministratorUserName,
            password,
            isPersistent: false,
            lockoutOnFailure: true);

        cancellationToken.ThrowIfCancellationRequested();

        if (!result.Succeeded)
        {
            var failure = result.IsLockedOut
                ? SignInFailure.LockedOut
                : result.IsNotAllowed
                    ? SignInFailure.NotAllowed
                    : SignInFailure.InvalidCredentials;
            logger.LogWarning(
                "Administrator sign-in failed with reason {Failure}",
                failure);
            return new SignInOutcome(false, false, failure);
        }

        var user = await userManager.Users
            .AsNoTracking()
            .SingleAsync(
                item => item.NormalizedUserName == BootstrapIdentity.NormalizedAdministratorUserName,
                cancellationToken);

        logger.LogInformation(
            "Administrator {UserId} signed in; forced password change is {MustChangePassword}",
            user.Id,
            user.MustChangePassword);
        return new SignInOutcome(true, user.MustChangePassword, SignInFailure.None);
    }

    public async Task<AuthenticatedUser?> GetUserAsync(
        Guid userId,
        CancellationToken cancellationToken)
    {
        return await userManager.Users
            .AsNoTracking()
            .Where(user => user.Id == userId)
            .Select(user => new AuthenticatedUser(
                user.Id,
                user.UserName ?? AuthenticationDefaults.AdministratorUserName,
                user.MustChangePassword))
            .SingleOrDefaultAsync(cancellationToken);
    }

    public async Task<bool> VerifyPasswordAsync(
        Guid userId,
        string password,
        CancellationToken cancellationToken)
    {
        var user = await userManager.Users.SingleOrDefaultAsync(
            item => item.Id == userId,
            cancellationToken);
        if (user is null)
        {
            return false;
        }

        var valid = await userManager.CheckPasswordAsync(user, password);
        cancellationToken.ThrowIfCancellationRequested();
        logger.LogInformation(
            "Administrator {UserId} sensitive action reauthentication result was {Result}",
            userId,
            valid ? "Succeeded" : "Failed");
        return valid;
    }

    public async Task<PasswordChangeOutcome> ChangePasswordAsync(
        Guid userId,
        string currentPassword,
        string newPassword,
        CancellationToken cancellationToken)
    {
        await using var transaction = await database.Database.BeginTransactionAsync(cancellationToken);
        var user = await userManager.Users.SingleOrDefaultAsync(
            item => item.Id == userId,
            cancellationToken);

        if (user is null)
        {
            return new PasswordChangeOutcome(false, ["UserNotFound"]);
        }

        if (string.Equals(currentPassword, newPassword, StringComparison.Ordinal))
        {
            return new PasswordChangeOutcome(false, ["NewPasswordMatchesCurrent"]);
        }

        var result = await userManager.ChangePasswordAsync(user, currentPassword, newPassword);
        cancellationToken.ThrowIfCancellationRequested();

        if (!result.Succeeded)
        {
            return new PasswordChangeOutcome(
                false,
                result.Errors.Select(error => error.Code).ToArray());
        }

        user.MustChangePassword = false;
        user.SecurityVersion++;

        var updateResult = await userManager.UpdateAsync(user);
        cancellationToken.ThrowIfCancellationRequested();

        if (!updateResult.Succeeded)
        {
            return new PasswordChangeOutcome(
                false,
                updateResult.Errors.Select(error => error.Code).ToArray());
        }

        await transaction.CommitAsync(cancellationToken);
        await signInManager.SignOutAsync();
        logger.LogInformation(
            "Administrator {UserId} changed the initial password and the temporary session was revoked",
            user.Id);

        return new PasswordChangeOutcome(true, []);
    }

    public async Task SignOutAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        await signInManager.SignOutAsync();
    }
}

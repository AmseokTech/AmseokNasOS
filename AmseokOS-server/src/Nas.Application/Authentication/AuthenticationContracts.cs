//--------------------------//
//--------定义身份认证与强制改密的应用边界---------//
//--------Defines application boundaries for authentication and forced password changes--------//
//-------------------------//
namespace Nas.Application.Authentication;

public static class AuthenticationDefaults
{
    public const string AdministratorUserName = "admin";
    public const string PasswordChangedPolicy = "PasswordChanged";
    public const string PasswordChangeSessionPolicy = "PasswordChangeSession";
    public const string TerminalAccessPolicy = "TerminalAccess";
    public const string SystemReadPolicy = "SystemRead";
    public const string NetworkReadPolicy = "NetworkRead";
    public const string NetworkManagePolicy = "NetworkManage";
    public const string StorageReadPolicy = "StorageRead";
    public const string StorageManagePolicy = "StorageManage";
    public const string RaidManagePolicy = "RaidManage";
    public const string MustChangePasswordClaim = "amseoknas:must_change_password";
    public const string PermissionClaim = "amseoknas:permission";
}

public enum SignInFailure
{
    None,
    InvalidCredentials,
    LockedOut,
    NotAllowed
}

public sealed record SignInOutcome(
    bool Succeeded,
    bool MustChangePassword,
    SignInFailure Failure);

public sealed record AuthenticatedUser(
    Guid Id,
    string UserName,
    bool MustChangePassword);

public sealed record PasswordChangeOutcome(
    bool Succeeded,
    IReadOnlyCollection<string> ErrorCodes);

public interface IAuthenticationService
{
    Task<SignInOutcome> SignInAdministratorAsync(
        string password,
        CancellationToken cancellationToken);

    Task<AuthenticatedUser?> GetUserAsync(
        Guid userId,
        CancellationToken cancellationToken);

    Task<bool> VerifyPasswordAsync(
        Guid userId,
        string password,
        CancellationToken cancellationToken);

    Task<PasswordChangeOutcome> ChangePasswordAsync(
        Guid userId,
        string currentPassword,
        string newPassword,
        CancellationToken cancellationToken);

    Task SignOutAsync(CancellationToken cancellationToken);
}

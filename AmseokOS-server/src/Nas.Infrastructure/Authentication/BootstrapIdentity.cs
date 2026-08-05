//--------------------------//
//--------定义固定管理员身份种子且不保存初始密码明文---------//
//--------Defines the fixed administrator seed without storing the initial plaintext password--------//
//-------------------------//
using Nas.Application.Authentication;

namespace Nas.Infrastructure.Authentication;

internal static class BootstrapIdentity
{
    public static readonly Guid AdministratorUserId = Guid.Parse("9aeb2b37-3d2a-4f6f-9068-44ca9f20a301");
    public static readonly Guid AdministratorRoleId = Guid.Parse("3791e91b-bcd4-4f80-8f38-e9d7acb998c6");

    public const string AdministratorRoleName = "admin";

    // 该 Identity V3 哈希只对应一次性初始密码，用户改密后会被新哈希覆盖
    // This Identity V3 hash only represents the one-time initial password and is replaced after change
    public const string InitialPasswordHash =
        "AQAAAAIAAYagAAAAEHd07/lWDHilbuTDL9jBep584Mmn77geD/ljDTSGiuqv2WR1QYUhMAMSx3P9qas4Kg==";

    public static readonly DateTimeOffset CreatedAt =
        new(2026, 7, 18, 0, 0, 0, TimeSpan.Zero);

    public const string NormalizedAdministratorUserName = "ADMIN";
    public const string UserSecurityStamp = "0bb934c2-07fa-451d-923f-31b113e8fb6a";
    public const string UserConcurrencyStamp = "3e898478-8913-4472-a9cf-5d5e5d8e15f6";
    public const string RoleConcurrencyStamp = "2c7339d4-7a32-4cf2-9884-7ac8440dfe74";
}

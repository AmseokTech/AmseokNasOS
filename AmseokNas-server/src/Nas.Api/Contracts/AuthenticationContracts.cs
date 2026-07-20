//--------------------------//
//--------定义登录、会话与修改密码的 HTTP 契约---------//
//--------Defines HTTP contracts for sign-in, sessions, and password changes--------//
//-------------------------//
using System.ComponentModel.DataAnnotations;

namespace Nas.Api.Contracts;

public sealed record LoginRequest(
    [param: Required, MaxLength(256)] string Password);

public sealed record ChangePasswordRequest(
    [param: Required, MaxLength(256)] string CurrentPassword,
    [param: Required, MaxLength(256)] string NewPassword);

public sealed record AuthenticationResponse(
    string UserName,
    bool MustChangePassword);

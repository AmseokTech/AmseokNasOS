//--------------------------//
//--------定义受限系统查询适配器共享的配置与错误边界---------//
//--------Defines shared configuration and errors for constrained system-query adapters--------//
//-------------------------//
namespace Nas.Application.Privileged;

public sealed class PrivilegedOptions
{
    public const string SectionName = "Privileged";

    public bool Enabled { get; init; }
    public string SocketPath { get; init; } = "/run/amseoknas/privileged.sock";
    public int TimeoutSeconds { get; init; } = 5;
    public int RaidTimeoutSeconds { get; init; } = 60;
}

public sealed class PrivilegedClientException(
    string code,
    string message,
    bool retryable,
    Exception? innerException = null,
    string? diagnosticMessage = null) : Exception(message, innerException)
{
    public string Code { get; } = code;
    public bool Retryable { get; } = retryable;
    public string? DiagnosticMessage { get; } = diagnosticMessage;
}

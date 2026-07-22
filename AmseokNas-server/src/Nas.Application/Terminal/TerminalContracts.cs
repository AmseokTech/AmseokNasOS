//--------------------------//
//--------定义 Web Terminal 会话与 broker 应用边界---------//
//--------Defines Web Terminal session and broker application boundaries--------//
//-------------------------//
namespace Nas.Application.Terminal;

public sealed class TerminalOptions
{
    public const string SectionName = "Terminal";

    public bool Enabled { get; init; }
    public string SocketPath { get; init; } = "/run/amseoknas-terminal/terminal.sock";
    public string[] AllowedOrigins { get; init; } = [];
    public int PendingSessionLifetimeSeconds { get; init; } = 30;
    public int IdleTimeoutMinutes { get; init; } = 15;
    public int MaximumSessionMinutes { get; init; } = 60;
}

public sealed record PendingTerminalSession(
    Guid Id,
    Guid UserId,
    ushort Columns,
    ushort Rows,
    DateTimeOffset ExpiresAt);

public interface ITerminalSessionStore
{
    PendingTerminalSession Create(Guid userId, ushort columns, ushort rows);

    PendingTerminalSession? Consume(Guid sessionId, Guid userId);
}

public abstract record TerminalBrokerEvent;

public sealed record TerminalOutput(ReadOnlyMemory<byte> Data) : TerminalBrokerEvent;

public sealed record TerminalExited(int? ExitCode) : TerminalBrokerEvent;

public sealed record TerminalBrokerError(string Code, string Message) : TerminalBrokerEvent;

public interface ITerminalBrokerSession : IAsyncDisposable
{
    IAsyncEnumerable<TerminalBrokerEvent> ReadEventsAsync(CancellationToken cancellationToken);

    Task SendInputAsync(ReadOnlyMemory<byte> data, CancellationToken cancellationToken);

    Task ResizeAsync(ushort columns, ushort rows, CancellationToken cancellationToken);

    Task CloseAsync(CancellationToken cancellationToken);
}

public interface ITerminalBrokerClient
{
    Task<ITerminalBrokerSession> OpenAsync(
        Guid sessionId,
        ushort columns,
        ushort rows,
        CancellationToken cancellationToken);
}

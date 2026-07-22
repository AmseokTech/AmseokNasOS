//--------------------------//
//--------保存短期且一次性消费的节点终端会话---------//
//--------Stores short-lived node terminal sessions consumed exactly once--------//
//-------------------------//
using System.Collections.Concurrent;
using Microsoft.Extensions.Options;
using Nas.Application.Terminal;

namespace Nas.Infrastructure.Terminal;

public sealed class InMemoryTerminalSessionStore(
    IOptions<TerminalOptions> options,
    TimeProvider timeProvider) : ITerminalSessionStore
{
    private readonly ConcurrentDictionary<Guid, PendingTerminalSession> sessions = new();

    public PendingTerminalSession Create(Guid userId, ushort columns, ushort rows)
    {
        RemoveExpiredSessions();
        var session = new PendingTerminalSession(
            Guid.NewGuid(),
            userId,
            columns,
            rows,
            timeProvider.GetUtcNow().AddSeconds(options.Value.PendingSessionLifetimeSeconds));
        if (!sessions.TryAdd(session.Id, session))
        {
            throw new InvalidOperationException("Failed to allocate a unique terminal session ID");
        }

        return session;
    }

    public PendingTerminalSession? Consume(Guid sessionId, Guid userId)
    {
        if (!sessions.TryRemove(sessionId, out var session))
        {
            return null;
        }

        return session.UserId == userId && session.ExpiresAt > timeProvider.GetUtcNow()
            ? session
            : null;
    }

    private void RemoveExpiredSessions()
    {
        var now = timeProvider.GetUtcNow();
        foreach (var session in sessions)
        {
            if (session.Value.ExpiresAt <= now)
            {
                sessions.TryRemove(session.Key, out _);
            }
        }
    }
}

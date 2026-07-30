//--------------------------//
//--------编排终端重新认证与一次性授权会话---------//
//--------Orchestrates terminal reauthentication and one-time authorization sessions--------//
//-------------------------//
using Nas.Application.Authentication;

namespace Nas.Application.Terminal;

public sealed class TerminalSessionService(
    IAuthenticationService authentication,
    ITerminalSessionStore sessions,
    TerminalOptions options) : ITerminalSessionService
{
    public bool IsEnabled => options.Enabled;

    public async Task<TerminalSessionCreationOutcome> CreateAsync(
        Guid userId,
        string password,
        ushort columns,
        ushort rows,
        CancellationToken cancellationToken)
    {
        if (!IsEnabled)
        {
            return new TerminalSessionCreationRejected(
                TerminalSessionCreationFailure.Disabled);
        }

        if (!await authentication.VerifyPasswordAsync(
                userId,
                password,
                cancellationToken))
        {
            return new TerminalSessionCreationRejected(
                TerminalSessionCreationFailure.ReauthenticationFailed);
        }

        return new TerminalSessionCreated(
            sessions.Create(userId, columns, rows));
    }

    public TerminalSessionConsumptionOutcome Consume(Guid sessionId, Guid userId)
    {
        if (!IsEnabled)
        {
            return new TerminalSessionConsumptionRejected(
                TerminalSessionConsumptionFailure.Disabled);
        }

        var session = sessions.Consume(sessionId, userId);
        return session is null
            ? new TerminalSessionConsumptionRejected(
                TerminalSessionConsumptionFailure.Unavailable)
            : new TerminalSessionConsumed(session);
    }
}

//--------------------------//
//--------验证终端重新认证与一次性会话应用流程---------//
//--------Verifies terminal reauthentication and one-time session application flow--------//
//-------------------------//
using Nas.Application.Authentication;
using Nas.Application.Terminal;

namespace Nas.Api.Tests;

public sealed class TerminalSessionServiceTests
{
    [Fact]
    public async Task DisabledTerminalRejectsBeforeReauthentication()
    {
        var authentication = new AuthenticationServiceStub { PasswordIsValid = true };
        var sessions = new TerminalSessionStoreStub();
        var service = new TerminalSessionService(
            authentication,
            sessions,
            new TerminalOptions { Enabled = false });

        var outcome = await service.CreateAsync(
            Guid.NewGuid(),
            "secret",
            120,
            32,
            CancellationToken.None);

        var rejected = Assert.IsType<TerminalSessionCreationRejected>(outcome);
        Assert.Equal(TerminalSessionCreationFailure.Disabled, rejected.Failure);
        Assert.Equal(0, authentication.VerificationCount);
        Assert.Equal(0, sessions.CreationCount);
    }

    [Fact]
    public async Task FailedReauthenticationDoesNotCreateASession()
    {
        var authentication = new AuthenticationServiceStub { PasswordIsValid = false };
        var sessions = new TerminalSessionStoreStub();
        var service = new TerminalSessionService(
            authentication,
            sessions,
            new TerminalOptions { Enabled = true });

        var outcome = await service.CreateAsync(
            Guid.NewGuid(),
            "incorrect",
            120,
            32,
            CancellationToken.None);

        var rejected = Assert.IsType<TerminalSessionCreationRejected>(outcome);
        Assert.Equal(
            TerminalSessionCreationFailure.ReauthenticationFailed,
            rejected.Failure);
        Assert.Equal(1, authentication.VerificationCount);
        Assert.Equal(0, sessions.CreationCount);
    }

    [Fact]
    public async Task SuccessfulReauthenticationCreatesTheBoundedSession()
    {
        var authentication = new AuthenticationServiceStub { PasswordIsValid = true };
        var sessions = new TerminalSessionStoreStub();
        var service = new TerminalSessionService(
            authentication,
            sessions,
            new TerminalOptions { Enabled = true });
        var userId = Guid.NewGuid();

        var outcome = await service.CreateAsync(
            userId,
            "secret",
            100,
            28,
            CancellationToken.None);

        var created = Assert.IsType<TerminalSessionCreated>(outcome);
        Assert.Equal(userId, created.Session.UserId);
        Assert.Equal((ushort)100, created.Session.Columns);
        Assert.Equal((ushort)28, created.Session.Rows);
        Assert.Equal(1, authentication.VerificationCount);
        Assert.Equal(1, sessions.CreationCount);
        var consumed = Assert.IsType<TerminalSessionConsumed>(
            service.Consume(created.Session.Id, userId));
        Assert.Same(created.Session, consumed.Session);
        var unavailable = Assert.IsType<TerminalSessionConsumptionRejected>(
            service.Consume(created.Session.Id, userId));
        Assert.Equal(
            TerminalSessionConsumptionFailure.Unavailable,
            unavailable.Failure);
    }

    private sealed class AuthenticationServiceStub : IAuthenticationService
    {
        public bool PasswordIsValid { get; init; }
        public int VerificationCount { get; private set; }

        public Task<bool> VerifyPasswordAsync(
            Guid userId,
            string password,
            CancellationToken cancellationToken)
        {
            VerificationCount++;
            return Task.FromResult(PasswordIsValid);
        }

        public Task<SignInOutcome> SignInAdministratorAsync(
            string password,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<AuthenticatedUser?> GetUserAsync(
            Guid userId,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<PasswordChangeOutcome> ChangePasswordAsync(
            Guid userId,
            string currentPassword,
            string newPassword,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task SignOutAsync(CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }

    private sealed class TerminalSessionStoreStub : ITerminalSessionStore
    {
        private PendingTerminalSession? session;

        public int CreationCount { get; private set; }

        public PendingTerminalSession Create(
            Guid userId,
            ushort columns,
            ushort rows)
        {
            CreationCount++;
            session = new PendingTerminalSession(
                Guid.NewGuid(),
                userId,
                columns,
                rows,
                DateTimeOffset.UtcNow.AddSeconds(30));
            return session;
        }

        public PendingTerminalSession? Consume(Guid sessionId, Guid userId)
        {
            if (session?.Id != sessionId || session.UserId != userId)
            {
                return null;
            }

            var consumed = session;
            session = null;
            return consumed;
        }
    }
}

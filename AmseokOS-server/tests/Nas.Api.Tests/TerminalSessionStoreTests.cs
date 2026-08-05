//--------------------------//
//--------验证终端授权会话的所有者绑定和一次性消费---------//
//--------Verifies terminal grants are owner-bound and consumed once--------//
//-------------------------//
using Microsoft.Extensions.Options;
using Nas.Application.Terminal;
using Nas.Infrastructure.Terminal;

namespace Nas.Api.Tests;

public sealed class TerminalSessionStoreTests
{
    [Fact]
    public void SessionCanOnlyBeConsumedOnceByItsOwner()
    {
        var options = Options.Create(new TerminalOptions
        {
            PendingSessionLifetimeSeconds = 30
        });
        var store = new InMemoryTerminalSessionStore(options, TimeProvider.System);
        var ownerId = Guid.NewGuid();
        var session = store.Create(ownerId, 120, 32);

        Assert.Null(store.Consume(session.Id, Guid.NewGuid()));
        Assert.Null(store.Consume(session.Id, ownerId));

        var secondSession = store.Create(ownerId, 100, 28);
        var consumed = store.Consume(secondSession.Id, ownerId);
        Assert.NotNull(consumed);
        Assert.Equal((ushort)100, consumed.Columns);
        Assert.Null(store.Consume(secondSession.Id, ownerId));
    }

    [Fact]
    public void ExpiredSessionCannotOpenATerminal()
    {
        var clock = new MutableTimeProvider();
        var store = new InMemoryTerminalSessionStore(
            Options.Create(new TerminalOptions
            {
                PendingSessionLifetimeSeconds = 30
            }),
            clock);
        var ownerId = Guid.NewGuid();
        var session = store.Create(ownerId, 120, 32);

        clock.Advance(TimeSpan.FromSeconds(31));

        Assert.Null(store.Consume(session.Id, ownerId));
    }

    private sealed class MutableTimeProvider : TimeProvider
    {
        private DateTimeOffset utcNow = new(2026, 7, 22, 0, 0, 0, TimeSpan.Zero);

        public override DateTimeOffset GetUtcNow() => utcNow;

        public void Advance(TimeSpan duration) => utcNow += duration;
    }
}

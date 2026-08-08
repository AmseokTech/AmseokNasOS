//--------------------------//
//--------保存短期一次性 RAID 预览票据---------//
//--------Stores short-lived one-time RAID preview tickets--------//
//-------------------------//
using System.Collections.Concurrent;
using System.Security.Cryptography;
using Nas.Application.RaidManagement;

namespace Nas.Infrastructure.Privileged;

public sealed class InMemoryRaidPreviewStore(TimeProvider timeProvider) : IRaidPreviewStore
{
    private readonly ConcurrentDictionary<string, RaidPreviewTicket> tickets =
        new(StringComparer.Ordinal);

    public RaidPreviewTicket Store(
        Guid userId,
        RequestedRaidOperation requested,
        string? arrayDisplayName,
        IReadOnlyList<string> expectedMemberDeviceIds,
        IReadOnlyList<string> resourceIds,
        string snapshotFingerprint,
        string confirmationPhrase,
        DateTimeOffset expiresAt)
    {
        RemoveExpired(timeProvider.GetUtcNow());
        for (var attempt = 0; attempt < 4; attempt++)
        {
            var token = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))
                .TrimEnd('=')
                .Replace('+', '-')
                .Replace('/', '_');
            var ticket = new RaidPreviewTicket(
                token,
                userId,
                requested,
                arrayDisplayName,
                expectedMemberDeviceIds,
                resourceIds,
                snapshotFingerprint,
                confirmationPhrase,
                expiresAt);
            if (tickets.TryAdd(token, ticket))
            {
                return ticket;
            }
        }

        throw new InvalidOperationException("Unable to allocate a unique RAID preview token");
    }

    public RaidPreviewTicket? Consume(Guid userId, string token, DateTimeOffset now)
    {
        if (!tickets.TryRemove(token, out var ticket)
            || ticket.UserId != userId
            || ticket.ExpiresAt <= now)
        {
            return null;
        }
        return ticket;
    }

    private void RemoveExpired(DateTimeOffset now)
    {
        foreach (var ticket in tickets)
        {
            if (ticket.Value.ExpiresAt <= now)
            {
                tickets.TryRemove(ticket.Key, out _);
            }
        }
    }
}

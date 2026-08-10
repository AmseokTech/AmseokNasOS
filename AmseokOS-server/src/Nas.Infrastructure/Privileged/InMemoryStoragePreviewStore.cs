//--------------------------//
//--------保存短期一次性数据卷与共享预览票据---------//
//--------Stores short-lived one-time volume and share preview tickets--------//
//-------------------------//
using System.Collections.Concurrent;
using System.Security.Cryptography;
using Nas.Application.StorageManagement;

namespace Nas.Infrastructure.Privileged;

public sealed class InMemoryStoragePreviewStore(TimeProvider timeProvider) : IStoragePreviewStore
{
    private readonly ConcurrentDictionary<string, StoragePreviewTicket> tickets =
        new(StringComparer.Ordinal);

    public StoragePreviewTicket Store(
        Guid userId,
        RequestedStorageOperation requested,
        IReadOnlyList<string> resourceIds,
        string snapshotFingerprint,
        string confirmationPhrase,
        DateTimeOffset expiresAt)
    {
        RemoveExpired(timeProvider.GetUtcNow());
        for (var attempt = 0; attempt < 4; attempt++)
        {
            var token = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))
                .TrimEnd('=').Replace('+', '-').Replace('/', '_');
            var ticket = new StoragePreviewTicket(
                token,
                userId,
                requested,
                resourceIds,
                snapshotFingerprint,
                confirmationPhrase,
                expiresAt);
            if (tickets.TryAdd(token, ticket))
            {
                return ticket;
            }
        }
        throw new InvalidOperationException("Unable to allocate a unique storage preview token");
    }

    public StoragePreviewTicket? Consume(Guid userId, string token, DateTimeOffset now)
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

using System.Security.Cryptography;
using System.Text.Json;

namespace StatusTide.Subscriber.Features.Polling;

/// <summary>
/// A hash of a snapshot, so an unchanged poll can stop before touching the database.
/// It hashes our normalized snapshot rather than the raw body, so changes we do not
/// store (such as a vendor's update text or field order) never count as changes.
/// </summary>
public static class SnapshotFingerprint
{
    public static string Of(StatusSnapshot snapshot)
    {
        // Sort everything, so the same facts in a different order give the same hash.
        var normalized = new StatusSnapshot(
            [.. snapshot.Components.OrderBy(c => c.ExternalId, StringComparer.Ordinal)],
            [.. snapshot.Incidents
                .OrderBy(i => i.ExternalId, StringComparer.Ordinal)
                .Select(i => i with
                {
                    ComponentExternalIds = [.. i.ComponentExternalIds.Order(StringComparer.Ordinal)],
                    Updates = [.. i.Updates.OrderBy(u => u.OccurredAt).ThenBy(u => u.Status)],
                })]);

        return Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(normalized)));
    }
}

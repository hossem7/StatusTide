using StatusTide.Data.Incidents;
using StatusTide.Data.Vendors;
using StatusTide.Subscriber.Features.Polling;

namespace StatusTide.UnitTests.Polling;

public class SnapshotFingerprintTests
{
    static readonly DateTimeOffset Started = new(2026, 11, 2, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public void The_same_facts_in_a_different_order_have_the_same_fingerprint()
    {
        var snapshot = Snapshot(ComponentStatus.Operational);
        var shuffled = new StatusSnapshot(
            [.. snapshot.Components.Reverse()],
            [.. snapshot.Incidents.Reverse().Select(i => i with
            {
                ComponentExternalIds = [.. i.ComponentExternalIds.Reverse()],
                Updates = [.. i.Updates.Reverse()],
            })]);

        Assert.Equal(SnapshotFingerprint.Of(snapshot), SnapshotFingerprint.Of(shuffled));
    }

    [Fact]
    public void A_status_change_changes_the_fingerprint()
    {
        Assert.NotEqual(
            SnapshotFingerprint.Of(Snapshot(ComponentStatus.Operational)),
            SnapshotFingerprint.Of(Snapshot(ComponentStatus.MajorOutage)));
    }

    static StatusSnapshot Snapshot(ComponentStatus apiStatus) => new(
        [new("api", "API", apiStatus), new("web", "Website", ComponentStatus.Operational)],
        [
            Incident("inc-1", ["api", "web"]),
            Incident("inc-2", ["web"]),
        ]);

    static IncidentSnapshot Incident(string id, IReadOnlyList<string> components) => new(
        id,
        "Elevated errors",
        IncidentStatus.Identified,
        Started,
        null,
        $"https://status.nimbusmail.example/incidents/{id}",
        components,
        [
            new(IncidentStatus.Investigating, Started),
            new(IncidentStatus.Identified, Started.AddMinutes(10)),
        ]);
}

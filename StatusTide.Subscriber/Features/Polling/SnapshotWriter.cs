using Microsoft.EntityFrameworkCore;

using StatusTide.Data;
using StatusTide.Data.Incidents;
using StatusTide.Data.Vendors;

namespace StatusTide.Subscriber.Features.Polling;

/// <summary>
/// Brings a vendor's components and incidents in PostgreSQL in line with a snapshot.
/// Only real differences are written, so applying the same snapshot twice changes nothing.
/// </summary>
public class SnapshotWriter(StatusTideDbContext db)
{
    /// <summary>Saves the snapshot. Returns true when anything changed.</summary>
    /// <param name="vendor">A tracked vendor with its components loaded.</param>
    public async Task<bool> SaveAsync(Vendor vendor, StatusSnapshot snapshot, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var components = ApplyComponents(vendor, snapshot.Components, now);
        await ApplyIncidentsAsync(vendor, snapshot.Incidents, components, now, cancellationToken);

        if (!db.ChangeTracker.HasChanges())
        {
            return false;
        }

        await db.SaveChangesAsync(cancellationToken);
        return true;
    }

    /// <summary>Marks every component "unknown" after a failed read. Returns true when any status changed.</summary>
    public static bool MarkUnknown(Vendor vendor, DateTimeOffset now)
    {
        var changed = false;
        foreach (var component in vendor.Components.Where(c => c.Status != ComponentStatus.Unknown))
        {
            component.Status = ComponentStatus.Unknown;
            component.StatusChangedAt = now;
            changed = true;
        }

        return changed;
    }

    private Dictionary<string, Component> ApplyComponents(Vendor vendor, IReadOnlyList<ComponentSnapshot> snapshots, DateTimeOffset now)
    {
        var byExternalId = vendor.Components.ToDictionary(c => c.ExternalId);
        foreach (var snapshot in snapshots)
        {
            if (!byExternalId.TryGetValue(snapshot.ExternalId, out var component))
            {
                // Added to the DbSet, not only the navigation: the id is set in code, so EF Core
                // would otherwise take it for an existing row and try an UPDATE.
                component = new Component { VendorId = vendor.Id, ExternalId = snapshot.ExternalId, Name = snapshot.Name };
                db.Components.Add(component);
                byExternalId.Add(component.ExternalId, component);
            }

            component.Name = snapshot.Name;
            if (component.Status != snapshot.Status || component.StatusChangedAt == default)
            {
                component.Status = snapshot.Status;
                component.StatusChangedAt = now;
            }
        }

        // Components missing from the feed are kept: users may follow them, and a vendor
        // can drop a component from one response and bring it back in the next.
        return byExternalId;
    }

    private async Task ApplyIncidentsAsync(
        Vendor vendor,
        IReadOnlyList<IncidentSnapshot> snapshots,
        Dictionary<string, Component> components,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var externalIds = snapshots.Select(i => i.ExternalId).ToList();
        var incidents = await db.Incidents
            .Include(i => i.Components)
            .Include(i => i.Events)
            .Where(i => i.VendorId == vendor.Id && (i.ResolvedAt == null || externalIds.Contains(i.ExternalId)))
            .ToDictionaryAsync(i => i.ExternalId, cancellationToken);

        foreach (var snapshot in snapshots)
        {
            if (!incidents.TryGetValue(snapshot.ExternalId, out var incident))
            {
                incident = new Incident { VendorId = vendor.Id, ExternalId = snapshot.ExternalId, Title = snapshot.Title, SourceUrl = snapshot.SourceUrl };
                db.Incidents.Add(incident);
                incidents.Add(incident.ExternalId, incident);
            }

            // EF Core only writes properties whose value really changed.
            incident.Title = snapshot.Title;
            incident.Status = snapshot.Status;
            incident.StartedAt = snapshot.StartedAt;
            incident.ResolvedAt = snapshot.ResolvedAt;
            incident.SourceUrl = snapshot.SourceUrl;

            ApplyUpdates(incident, snapshot.Updates);
            ApplyAffectedComponents(incident, snapshot.ComponentExternalIds, components);
        }

        // Statuspage's summary lists only unresolved incidents, so one that leaves the feed was
        // resolved. The vendor's resolve time is not in the feed; our detection time stands in.
        var listed = externalIds.ToHashSet();
        foreach (var incident in incidents.Values.Where(i => i.ResolvedAt is null && !listed.Contains(i.ExternalId)))
        {
            incident.Status = IncidentStatus.Resolved;
            incident.ResolvedAt = now;
            incident.Events.Add(new IncidentEvent { Kind = IncidentEventKind.VendorUpdate, VendorStatus = IncidentStatus.Resolved, OccurredAt = now });
        }
    }

    /// <summary>Adds the vendor updates we have not stored yet: status and time only, never text.</summary>
    private static void ApplyUpdates(Incident incident, IReadOnlyList<IncidentUpdateSnapshot> updates)
    {
        var latest = incident.Events
            .Where(e => e.Kind == IncidentEventKind.VendorUpdate)
            .Select(e => (DateTimeOffset?)e.OccurredAt)
            .Max();

        foreach (var update in updates.Where(u => latest is null || u.OccurredAt > latest))
        {
            incident.Events.Add(new IncidentEvent { Kind = IncidentEventKind.VendorUpdate, VendorStatus = update.Status, OccurredAt = update.OccurredAt });
        }
    }

    private static void ApplyAffectedComponents(Incident incident, IReadOnlyList<string> externalIds, Dictionary<string, Component> components)
    {
        var wanted = externalIds.Select(components.GetValueOrDefault).OfType<Component>().ToHashSet();
        incident.Components.RemoveAll(c => !wanted.Contains(c));
        incident.Components.AddRange(wanted.Except(incident.Components));
    }
}

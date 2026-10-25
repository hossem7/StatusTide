using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

using StatusTide.Data.Vendors;

namespace StatusTide.Data.Incidents;

internal class IncidentConfiguration : IEntityTypeConfiguration<Incident>
{
    public void Configure(EntityTypeBuilder<Incident> incident)
    {
        incident.Property(i => i.ExternalId).HasMaxLength(200);
        incident.Property(i => i.Title).HasMaxLength(500);
        incident.Property(i => i.SourceUrl).HasMaxLength(2048);

        incident.HasIndex(i => new { i.VendorId, i.ExternalId }).IsUnique();

        // A vendor's incident history, newest first.
        incident.HasIndex(i => new { i.VendorId, i.StartedAt }).IsDescending(false, true);

        // Dashboards ask for open incidents on every load, and there are few of them.
        incident.HasIndex(i => i.VendorId)
            .HasDatabaseName("ix_incidents_vendor_id_open")
            .HasFilter("resolved_at IS NULL");

        incident.HasMany(i => i.Components)
            .WithMany()
            .UsingEntity(
                "incident_components",
                component => component.HasOne(typeof(Component)).WithMany().HasForeignKey("ComponentId"),
                incidentSide => incidentSide.HasOne(typeof(Incident)).WithMany().HasForeignKey("IncidentId"));
    }
}

internal class IncidentEventConfiguration : IEntityTypeConfiguration<IncidentEvent>
{
    public void Configure(EntityTypeBuilder<IncidentEvent> incidentEvent)
    {
        incidentEvent.HasIndex(e => new { e.IncidentId, e.OccurredAt });
    }
}

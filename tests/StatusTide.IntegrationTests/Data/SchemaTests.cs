using Microsoft.EntityFrameworkCore;

using Npgsql;

using StatusTide.Data;
using StatusTide.Data.Alerts;
using StatusTide.Data.Follows;
using StatusTide.Data.Incidents;
using StatusTide.Data.Seed;
using StatusTide.Data.Users;
using StatusTide.Data.Vendors;

namespace StatusTide.IntegrationTests.Data;

/// <summary>
/// Checks the rules the database enforces for us. Each test creates its own users and
/// incidents, so tests can share the migrated database without seeing each other's rows.
/// </summary>
public sealed class SchemaTests(PostgresFixture postgres)
{
    private static readonly DateTimeOffset Now = new(2026, 10, 25, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Demo_vendor_is_seeded_with_operational_components()
    {
        await using var db = postgres.CreateDbContext();

        var vendor = await db.Vendors
            .AsNoTracking()
            .Include(v => v.Components)
            .Include(v => v.Verifications)
            .SingleAsync(v => v.Slug == "acme-cloud", TestContext.Current.CancellationToken);

        Assert.Equal(AcmeCloudSeed.VendorId, vendor.Id);
        Assert.Equal(FeedFormat.Demo, vendor.Format);
        Assert.True(vendor.IsCatalog);
        Assert.Equal(4, vendor.Components.Count);
        Assert.All(vendor.Components, c => Assert.Equal(ComponentStatus.Operational, c.Status));
        Assert.Equal(VerificationDecision.Approved, Assert.Single(vendor.Verifications).Decision);
    }

    [Fact]
    public async Task Enums_are_stored_as_text()
    {
        await using var db = postgres.CreateDbContext();

        var format = await db.Database
            .SqlQuery<string>($"SELECT format AS \"Value\" FROM vendors WHERE slug = 'acme-cloud'")
            .SingleAsync(TestContext.Current.CancellationToken);

        Assert.Equal("Demo", format);
    }

    [Fact]
    public async Task Following_the_same_component_twice_is_rejected()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var db = postgres.CreateDbContext();
        var user = await AddGuestAsync(db, ct);

        db.Follows.Add(new Follow { UserId = user.Id, ComponentId = AcmeCloudSeed.ApiComponentId, CreatedAt = Now });
        await db.SaveChangesAsync(ct);
        db.Follows.Add(new Follow { UserId = user.Id, ComponentId = AcmeCloudSeed.ApiComponentId, CreatedAt = Now });

        await AssertUniqueViolationAsync(db, "ix_follows_user_id_component_id", ct);
    }

    [Fact]
    public async Task The_same_alert_cannot_be_recorded_twice()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var db = postgres.CreateDbContext();
        var user = await AddGuestAsync(db, ct);
        var incident = await AddIncidentAsync(db, ct);

        db.AlertDeliveries.Add(Alert(user, incident));
        await db.SaveChangesAsync(ct);
        db.AlertDeliveries.Add(Alert(user, incident));

        await AssertUniqueViolationAsync(db, "ix_alert_deliveries_user_id_incident_id_channel_kind", ct);
    }

    [Fact]
    public async Task Deleting_a_guest_also_deletes_their_follows_and_alerts()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var db = postgres.CreateDbContext();
        var user = await AddGuestAsync(db, ct);
        var incident = await AddIncidentAsync(db, ct);
        db.Follows.Add(new Follow { UserId = user.Id, ComponentId = AcmeCloudSeed.ApiComponentId, CreatedAt = Now });
        db.AlertDeliveries.Add(Alert(user, incident));
        await db.SaveChangesAsync(ct);

        // The guest cleanup job deletes users with a set-based delete, so cascades must happen in the database.
        await db.Users.Where(u => u.Id == user.Id).ExecuteDeleteAsync(ct);

        Assert.False(await db.Follows.AnyAsync(f => f.UserId == user.Id, ct));
        Assert.False(await db.AlertDeliveries.AnyAsync(d => d.UserId == user.Id, ct));
    }

    [Fact]
    public async Task Incident_keeps_its_affected_components_and_timeline()
    {
        var ct = TestContext.Current.CancellationToken;
        Guid incidentId;
        await using (var db = postgres.CreateDbContext())
        {
            var incident = await AddIncidentAsync(db, ct);
            incident.Components.Add(await db.Components.SingleAsync(c => c.Id == AcmeCloudSeed.WebhooksComponentId, ct));
            incident.Events.Add(new IncidentEvent { Kind = IncidentEventKind.CheckFailing, OccurredAt = Now.AddMinutes(-4) });
            incident.Events.Add(new IncidentEvent
            {
                Kind = IncidentEventKind.VendorUpdate,
                VendorStatus = IncidentStatus.Investigating,
                OccurredAt = Now,
            });
            await db.SaveChangesAsync(ct);
            incidentId = incident.Id;
        }

        await using var readDb = postgres.CreateDbContext();
        var saved = await readDb.Incidents
            .AsNoTracking()
            .Include(i => i.Components)
            .Include(i => i.Events.OrderBy(e => e.OccurredAt))
            .SingleAsync(i => i.Id == incidentId, ct);

        Assert.Equal("Webhooks", Assert.Single(saved.Components).Name);
        Assert.Equal([IncidentEventKind.CheckFailing, IncidentEventKind.VendorUpdate], saved.Events.Select(e => e.Kind));
        Assert.Null(saved.Events[0].VendorStatus);
    }

    private static async Task<User> AddGuestAsync(StatusTideDbContext db, CancellationToken ct)
    {
        var user = new User { IsGuest = true, CreatedAt = Now, LastSeenAt = Now };
        db.Users.Add(user);
        await db.SaveChangesAsync(ct);
        return user;
    }

    private static async Task<Incident> AddIncidentAsync(StatusTideDbContext db, CancellationToken ct)
    {
        var incident = new Incident
        {
            VendorId = AcmeCloudSeed.VendorId,
            ExternalId = Guid.NewGuid().ToString(),
            Title = "Elevated webhook delivery errors",
            Status = IncidentStatus.Investigating,
            StartedAt = Now,
            SourceUrl = "/vendors/acme-cloud",
        };
        db.Incidents.Add(incident);
        await db.SaveChangesAsync(ct);
        return incident;
    }

    private static AlertDelivery Alert(User user, Incident incident) => new()
    {
        UserId = user.Id,
        IncidentId = incident.Id,
        Channel = AlertChannel.InApp,
        Kind = AlertKind.Started,
        SentAt = Now,
    };

    private static async Task AssertUniqueViolationAsync(StatusTideDbContext db, string indexName, CancellationToken ct)
    {
        var error = await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync(ct));
        var postgresError = Assert.IsType<PostgresException>(error.InnerException);
        Assert.Equal(PostgresErrorCodes.UniqueViolation, postgresError.SqlState);
        Assert.Equal(indexName, postgresError.ConstraintName);
    }
}

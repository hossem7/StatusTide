using Microsoft.Extensions.DependencyInjection;
using StatusTide.Data;
using StatusTide.Data.Follows;
using StatusTide.Data.Incidents;
using StatusTide.Data.Users;
using StatusTide.Data.Vendors;

namespace StatusTide.IntegrationTests.Dashboard;

/// <summary>
/// Adds made-up ".example" vendors, users and follows through the API's own database. Every
/// call makes fresh rows, so tests sharing a database never see each other's data.
/// </summary>
public sealed class DashboardSeed(ApiFactory factory)
{
    public static readonly DateTimeOffset Now = new(2026, 11, 2, 12, 0, 0, TimeSpan.Zero);

    public async Task<Guid> AddGuestAsync()
    {
        var user = new User { IsGuest = true, CreatedAt = Now, LastSeenAt = Now };
        await SaveAsync(db => db.Users.Add(user));
        return user.Id;
    }

    /// <summary>A disabled vendor (never polled) with one component per status, named after its status.</summary>
    public async Task<Vendor> AddVendorAsync(string name, params ComponentStatus[] statuses)
    {
        var slug = $"{name.ToLowerInvariant()}-{Guid.NewGuid():N}";
        var vendor = new Vendor
        {
            Slug = slug,
            Name = name,
            FeedUrl = $"https://status.{slug}.example/api/v2/summary.json",
            StatusPageUrl = $"https://status.{slug}.example",
            Format = FeedFormat.Statuspage,
            IsCatalog = true,
            IsEnabled = false,
            PollIntervalSeconds = 60,
            CreatedAt = Now,
        };
        for (var i = 0; i < statuses.Length; i++)
        {
            vendor.Components.Add(new Component
            {
                ExternalId = $"c{i}",
                Name = $"Component {i}",
                Status = statuses[i],
                StatusChangedAt = Now,
            });
        }

        await SaveAsync(db => db.Vendors.Add(vendor));
        return vendor;
    }

    public Task FollowAsync(Guid userId, params Component[] components) => FollowAsync(userId, [.. components.Select(c => c.Id)]);

    public Task FollowAsync(Guid userId, params Guid[] componentIds) =>
        SaveAsync(db => db.Follows.AddRange(componentIds.Select(id => new Follow { UserId = userId, ComponentId = id, CreatedAt = Now })));

    /// <summary>An incident on the given components, or on none when <paramref name="components"/> is empty.</summary>
    public async Task<Incident> AddIncidentAsync(
        Vendor vendor,
        string title,
        DateTimeOffset startedAt,
        DateTimeOffset? resolvedAt = null,
        params Component[] components)
    {
        var incident = new Incident
        {
            VendorId = vendor.Id,
            ExternalId = Guid.NewGuid().ToString("N"),
            Title = title,
            Status = resolvedAt is null ? IncidentStatus.Investigating : IncidentStatus.Resolved,
            StartedAt = startedAt,
            ResolvedAt = resolvedAt,
            SourceUrl = $"{vendor.StatusPageUrl}/incidents/{Guid.NewGuid():N}",
        };
        await SaveAsync(db =>
        {
            db.Components.AttachRange(components);
            incident.Components.AddRange(components);
            db.Incidents.Add(incident);
        });
        return incident;
    }

    public Task SetStatusAsync(Component component, ComponentStatus status) => SaveAsync(db =>
    {
        db.Components.Attach(component);
        component.Status = status;
        component.StatusChangedAt = Now;
    });

    private async Task SaveAsync(Action<StatusTideDbContext> change)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<StatusTideDbContext>();
        change(db);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
    }
}

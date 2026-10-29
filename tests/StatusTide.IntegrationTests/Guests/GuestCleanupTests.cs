extern alias Publisher;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

using Publisher::StatusTide.Publisher.Features.Guests;

using StatusTide.Data;
using StatusTide.Data.Follows;
using StatusTide.Data.Seed;
using StatusTide.Data.Users;

namespace StatusTide.IntegrationTests.Guests;

public sealed class GuestCleanupTests(PostgresFixture postgres) : IAsyncLifetime
{
    private static readonly DateTimeOffset Now = new(2026, 11, 2, 3, 0, 0, TimeSpan.Zero);

    private StatusTideDbContext _db = null!;

    // A database per test, since the cleanup deletes across the whole users table.
    public async ValueTask InitializeAsync() => _db = await postgres.CreateMigratedDbContextAsync($"cleanup_{Guid.NewGuid():N}");

    public ValueTask DisposeAsync() => _db.DisposeAsync();

    private static CancellationToken Cancel => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Deletes_guests_inactive_for_30_days_with_their_follows()
    {
        await AddUserAsync(isGuest: true, lastSeen: Now - GuestPolicy.Lifetime - TimeSpan.FromMinutes(1), follow: true);
        var recent = await AddUserAsync(isGuest: true, lastSeen: Now - GuestPolicy.Lifetime + TimeSpan.FromMinutes(1), follow: true);

        var deleted = await CreateCleanup().DeleteInactiveGuestsAsync(Cancel);

        Assert.Equal(1, deleted);
        Assert.Equal([recent], await _db.Users.Select(u => u.Id).ToListAsync(Cancel));
        Assert.Equal([recent], await _db.Follows.Select(f => f.UserId).ToListAsync(Cancel));
    }

    [Fact]
    public async Task Never_deletes_an_account()
    {
        var account = await AddUserAsync(isGuest: false, lastSeen: Now.AddYears(-1), follow: false);

        var deleted = await CreateCleanup().DeleteInactiveGuestsAsync(Cancel);

        Assert.Equal(0, deleted);
        Assert.Equal([account], await _db.Users.Select(u => u.Id).ToListAsync(Cancel));
    }

    private GuestCleanup CreateCleanup() => new(_db, new FakeTimeProvider(Now), NullLogger<GuestCleanup>.Instance);

    private async Task<Guid> AddUserAsync(bool isGuest, DateTimeOffset lastSeen, bool follow)
    {
        var user = new User
        {
            IsGuest = isGuest,
            Email = isGuest ? null : $"{Guid.NewGuid():N}@example.com",
            CreatedAt = lastSeen,
            LastSeenAt = lastSeen,
        };
        _db.Users.Add(user);
        if (follow)
        {
            _db.Follows.Add(new Follow { UserId = user.Id, ComponentId = AcmeCloudSeed.ApiComponentId, CreatedAt = lastSeen });
        }

        await _db.SaveChangesAsync(Cancel);
        _db.ChangeTracker.Clear();
        return user.Id;
    }
}

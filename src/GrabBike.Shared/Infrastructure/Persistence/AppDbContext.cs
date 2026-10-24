using System.Text.Json;
using GrabBike.Api.Features.Dev;
using GrabBike.Api.Features.Dispatch;
using GrabBike.Api.Features.Drivers;
using GrabBike.Api.Features.Maps;
using GrabBike.Api.Features.Rides;
using GrabBike.Api.SharedKernel;
using Microsoft.EntityFrameworkCore;
using NetTopologySuite.Geometries;

namespace GrabBike.Api.Infrastructure.Persistence;

public sealed class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options), IUnitOfWork
{
    private static readonly JsonSerializerOptions RouteJson = new(JsonSerializerDefaults.Web);
    private static readonly GeoRoute EmptyRoute = new("LineString", Array.Empty<double[]>());

    public DbSet<User> Users => Set<User>();
    public DbSet<Driver> Drivers => Set<Driver>();
    public DbSet<Place> Places => Set<Place>();
    public DbSet<Quote> Quotes => Set<Quote>();
    public DbSet<Ride> Rides => Set<Ride>();
    public DbSet<Offer> Offers => Set<Offer>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<User>(entity =>
        {
            entity.HasKey(user => user.Id);
            entity.Property(user => user.Role).HasConversion<string>().HasMaxLength(16);
            entity.Property(user => user.Name).HasMaxLength(120);
            entity.Property(user => user.City).HasMaxLength(40);
        });

        modelBuilder.Entity<Driver>(entity =>
        {
            entity.HasKey(driver => driver.UserId);
            entity.Property(driver => driver.Status).HasConversion<string>().HasMaxLength(16);
            entity.Property(driver => driver.Name).HasMaxLength(120);
            entity.Property(driver => driver.Plate).HasMaxLength(32);
            entity.Property(driver => driver.City).HasMaxLength(40);
            entity.Property(driver => driver.Position).HasColumnType("geography(point,4326)");
            entity.HasOne<User>().WithOne().HasForeignKey<Driver>(driver => driver.UserId);
        });

        modelBuilder.Entity<Place>(entity =>
        {
            entity.HasKey(place => place.Id);
            entity.Property(place => place.Id).HasMaxLength(64);
            entity.Property(place => place.Name).HasMaxLength(160);
            entity.Property(place => place.Address).HasMaxLength(240);
        });

        modelBuilder.Entity<Quote>(entity =>
        {
            entity.HasKey(quote => quote.Id);
            entity.OwnsOne(quote => quote.Pickup);
            entity.OwnsOne(quote => quote.Dropoff);
            entity.Property(quote => quote.Route).HasConversion(
                route => JsonSerializer.Serialize(route, RouteJson),
                json => JsonSerializer.Deserialize<GeoRoute>(json, RouteJson) ?? EmptyRoute);
        });

        modelBuilder.Entity<Ride>(entity =>
        {
            entity.HasKey(ride => ride.Id);
            entity.Property(ride => ride.Status).HasConversion<string>().HasMaxLength(24);
            entity.OwnsOne(ride => ride.Pickup);
            entity.OwnsOne(ride => ride.Dropoff);
            entity.Property(ride => ride.Route).HasConversion(
                route => JsonSerializer.Serialize(route, RouteJson),
                json => JsonSerializer.Deserialize<GeoRoute>(json, RouteJson) ?? EmptyRoute);
            entity.Property(ride => ride.ApproachRoute).HasConversion(
                route => route == null ? null : JsonSerializer.Serialize(route, RouteJson),
                json => string.IsNullOrWhiteSpace(json) ? null : JsonSerializer.Deserialize<GeoRoute>(json, RouteJson));
            entity.HasIndex(ride => ride.RiderId);
        });

        modelBuilder.Entity<Offer>(entity =>
        {
            entity.HasKey(offer => offer.Id);
            entity.Property(offer => offer.Status).HasConversion<string>().HasMaxLength(16);
            entity.HasIndex(offer => new { offer.RideId, offer.DriverId });
        });
    }

    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        SyncDriverPositions();
        return base.SaveChanges(acceptAllChangesOnSuccess);
    }

    public override Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
    {
        SyncDriverPositions();
        return base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
    }

    private void SyncDriverPositions()
    {
        foreach (var entry in ChangeTracker.Entries<Driver>())
        {
            if (entry.State is EntityState.Added or EntityState.Modified)
                entry.Entity.Position = new Point(entry.Entity.Lng, entry.Entity.Lat) { SRID = 4326 };
        }
    }
}

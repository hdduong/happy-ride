using GrabBike.Api.Features.Dev;
using GrabBike.Api.Features.Drivers;
using GrabBike.Api.Features.Maps;
using Microsoft.EntityFrameworkCore;

namespace GrabBike.Api.Infrastructure.Persistence;

public static class DatabaseSeeder
{
    // First name is the city code plus role and sequence. Last name is the role plus the same sequence.
    private static readonly Persona[] People =
    [
        new("11111111-1111-1111-1111-111111111111", "HCMR01 Rider01", UserRole.Rider, "HCMC", null, null, null),
        new("22222222-2222-2222-2222-222222222222", "HCMR02 Rider02", UserRole.Rider, "HCMC", null, null, null),
        new("33333333-3333-3333-3333-333333333333", "HCMD01 Driver01", UserRole.Driver, "HCMC", "59-H1 234.56", 10.7735, 106.6990),
        new("44444444-4444-4444-4444-444444444444", "HCMD02 Driver02", UserRole.Driver, "HCMC", "59-P2 118.20", 10.7705, 106.6965),
        new("55555555-5555-5555-5555-555555555555", "HCMD03 Driver03", UserRole.Driver, "HCMC", "59-H1 908.11", 10.7740, 106.7010),
        new("66666666-6666-6666-6666-666666666666", "HCMD04 Driver04", UserRole.Driver, "HCMC", "59-N1 441.07", 10.7690, 106.7000),
        new("77777777-7777-7777-7777-777777777777", "HanoiR01 Rider01", UserRole.Rider, "Hanoi", null, null, null),
        new("88888888-8888-8888-8888-888888888888", "HanoiR02 Rider02", UserRole.Rider, "Hanoi", null, null, null),
        new("99999999-9999-9999-9999-999999999999", "HanoiD01 Driver01", UserRole.Driver, "Hanoi", "29-H1 100.01", 21.02980, 105.85360),
        new("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa", "HanoiD02 Driver02", UserRole.Driver, "Hanoi", "29-P1 200.02", 21.02770, 105.85110),
        new("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb", "HanoiD03 Driver03", UserRole.Driver, "Hanoi", "29-H1 300.03", 21.03050, 105.85080),
        new("cccccccc-cccc-cccc-cccc-cccccccccccc", "HanoiD04 Driver04", UserRole.Driver, "Hanoi", "29-N1 400.04", 21.02680, 105.85430),
    ];

    public static async Task SeedAsync(AppDbContext db, CancellationToken cancellationToken = default)
    {
        if (await db.Users.AnyAsync(cancellationToken))
        {
            await RenameDemoPeopleAsync(db, cancellationToken);
            return;
        }

        foreach (var person in People)
        {
            if (person.Role == UserRole.Rider)
                AddRider(db, person.Id, person.Name, person.City);
            else
                AddDriver(db, person.Id, person.Name, person.Plate!, person.City, person.Lat!.Value, person.Lng!.Value);
        }

        db.Places.AddRange(
            new Place { Id = "place-ben-thanh", Name = "Chợ Bến Thành", Address = "District 1", Lat = 10.77254, Lng = 106.69800 },
            new Place { Id = "place-notre-dame", Name = "Nhà thờ Đức Bà", Address = "District 1", Lat = 10.77979, Lng = 106.69902 },
            new Place { Id = "place-bitexco", Name = "Bitexco Financial Tower", Address = "2 Hai Trieu, District 1", Lat = 10.77175, Lng = 106.70446 },
            new Place { Id = "place-hoan-kiem", Name = "Hồ Hoàn Kiếm", Address = "Hanoi", Lat = 21.02870, Lng = 105.85240 });

        await db.SaveChangesAsync(cancellationToken);
    }

    private static async Task RenameDemoPeopleAsync(AppDbContext db, CancellationToken cancellationToken)
    {
        foreach (var person in People)
        {
            var id = Guid.Parse(person.Id);
            var user = await db.Users.FindAsync([id], cancellationToken);
            if (user is not null)
                user.Name = person.Name;
            if (person.Role != UserRole.Driver)
                continue;
            var driver = await db.Drivers.FindAsync([id], cancellationToken);
            if (driver is not null)
                driver.Name = person.Name;
        }

        await db.SaveChangesAsync(cancellationToken);
    }

    private static void AddRider(AppDbContext db, string id, string name, string city)
    {
        db.Users.Add(new User
        {
            Id = Guid.Parse(id),
            Name = name,
            Role = UserRole.Rider,
            City = city,
        });
    }

    private static void AddDriver(AppDbContext db, string id, string name, string plate, string city, double lat, double lng)
    {
        var userId = Guid.Parse(id);
        db.Users.Add(new User
        {
            Id = userId,
            Name = name,
            Role = UserRole.Driver,
            City = city,
        });
        db.Drivers.Add(new Driver
        {
            UserId = userId,
            Name = name,
            Plate = plate,
            City = city,
            Status = DriverStatus.Online,
            Lat = lat,
            Lng = lng,
        });
    }

    private sealed record Persona(
        string Id,
        string Name,
        UserRole Role,
        string City,
        string? Plate,
        double? Lat,
        double? Lng);
}

using GrabBike.Api.Features.Dev;
using GrabBike.Api.Features.Drivers;
using GrabBike.Api.Features.Maps;
using Microsoft.EntityFrameworkCore;

namespace GrabBike.Api.Infrastructure.Persistence;

public static class DatabaseSeeder
{
    public static async Task SeedAsync(AppDbContext db, CancellationToken cancellationToken = default)
    {
        if (await db.Users.AnyAsync(cancellationToken))
            return;

        AddRider(db, "11111111-1111-1111-1111-111111111111", "An Nguyễn", "HCMC");
        AddRider(db, "22222222-2222-2222-2222-222222222222", "Mai Trần", "HCMC");
        AddDriver(db, "33333333-3333-3333-3333-333333333333", "Khoa Phạm", "59-H1 234.56", "HCMC", 10.7735, 106.6990);
        AddDriver(db, "44444444-4444-4444-4444-444444444444", "Linh Võ", "59-P2 118.20", "HCMC", 10.7705, 106.6965);
        AddDriver(db, "55555555-5555-5555-5555-555555555555", "Đức Lê", "59-H1 908.11", "HCMC", 10.7740, 106.7010);
        AddDriver(db, "66666666-6666-6666-6666-666666666666", "Hà Bùi", "59-N1 441.07", "HCMC", 10.7690, 106.7000);
        AddRider(db, "77777777-7777-7777-7777-777777777777", "Nam Đỗ", "Hanoi");
        AddRider(db, "88888888-8888-8888-8888-888888888888", "Hoa Lý", "Hanoi");
        AddDriver(db, "99999999-9999-9999-9999-999999999999", "Minh Trần", "29-H1 100.01", "Hanoi", 21.02980, 105.85360);
        AddDriver(db, "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa", "Quân Phạm", "29-P1 200.02", "Hanoi", 21.02770, 105.85110);
        AddDriver(db, "bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb", "Lan Ngô", "29-H1 300.03", "Hanoi", 21.03050, 105.85080);
        AddDriver(db, "cccccccc-cccc-cccc-cccc-cccccccccccc", "Sơn Vũ", "29-N1 400.04", "Hanoi", 21.02680, 105.85430);

        db.Places.AddRange(
            new Place { Id = "place-ben-thanh", Name = "Chợ Bến Thành", Address = "District 1", Lat = 10.77254, Lng = 106.69800 },
            new Place { Id = "place-notre-dame", Name = "Nhà thờ Đức Bà", Address = "District 1", Lat = 10.77979, Lng = 106.69902 },
            new Place { Id = "place-bitexco", Name = "Bitexco Financial Tower", Address = "2 Hai Trieu, District 1", Lat = 10.77175, Lng = 106.70446 },
            new Place { Id = "place-hoan-kiem", Name = "Hồ Hoàn Kiếm", Address = "Hanoi", Lat = 21.02870, Lng = 105.85240 });

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
}

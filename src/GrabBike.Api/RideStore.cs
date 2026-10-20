namespace GrabBike.Api;

public sealed class RideStore
{
    private const double DispatchRadiusM = 3000;
    private const int OfferTtlSeconds = 30;
    private const int MaxOffers = 3;
    private const int MaxRounds = 3;

    private readonly object _gate = new();
    private readonly List<User> _users = [];
    private readonly List<Driver> _drivers = [];
    private readonly List<Place> _places = [];
    private readonly List<Quote> _quotes = [];
    private readonly List<Ride> _rides = [];
    private readonly List<Offer> _offers = [];

    public RideStore()
    {
        Seed();
    }

    public User? FindUser(Guid id)
    {
        lock (_gate)
            return _users.FirstOrDefault(user => user.Id == id);
    }

    public IReadOnlyList<DevUserDto> ListUsers()
    {
        lock (_gate)
        {
            var users = new List<DevUserDto>(_users.Count);
            foreach (var user in _users)
            {
                string? plate = null;
                if (user.Role == UserRole.Driver)
                    plate = FindDriver(user.Id)?.Plate;
                users.Add(new DevUserDto(user.Id, user.Name, user.Role.ToString(), user.City, plate));
            }

            return users;
        }
    }

    public IReadOnlyList<PlaceDto> Autocomplete(string? query, double? lat, double? lng)
    {
        lock (_gate)
        {
            var text = (query ?? "").Trim();
            var matches = new List<Place>();
            foreach (var place in _places)
            {
                if (text.Length == 0 || Matches(place, text))
                    matches.Add(place);
            }

            if (lat is double biasLat && lng is double biasLng)
            {
                matches.Sort((left, right) =>
                {
                    var leftDistance = Geo.HaversineMeters(biasLat, biasLng, left.Lat, left.Lng);
                    var rightDistance = Geo.HaversineMeters(biasLat, biasLng, right.Lat, right.Lng);
                    var compared = leftDistance.CompareTo(rightDistance);
                    return compared != 0 ? compared : string.CompareOrdinal(left.Id, right.Id);
                });
            }

            return matches.Select(ToPlaceDto).ToList();
        }
    }

    public PlaceDto? GetPlace(string id)
    {
        lock (_gate)
        {
            var place = _places.FirstOrDefault(item => item.Id == id);
            return place is null ? null : ToPlaceDto(place);
        }
    }

    public string Reverse(double lat, double lng)
    {
        lock (_gate)
        {
            Place? nearest = null;
            var best = double.MaxValue;
            foreach (var place in _places)
            {
                var distance = Geo.HaversineMeters(lat, lng, place.Lat, place.Lng);
                if (distance < best)
                {
                    best = distance;
                    nearest = place;
                }
            }

            return nearest is null ? "" : $"{nearest.Name}, {nearest.Address}";
        }
    }

    public QuoteDto CreateQuote(GeoPoint pickup, GeoPoint dropoff)
    {
        var (distanceM, durationS, fareVnd) = Geo.Estimate(pickup, dropoff);
        var quote = new Quote
        {
            Id = Guid.NewGuid(),
            Provider = "Fake",
            Pickup = pickup,
            Dropoff = dropoff,
            DistanceM = distanceM,
            DurationS = durationS,
            FareVnd = fareVnd,
            Route = Geo.StraightRoute(pickup, dropoff),
        };

        lock (_gate)
            _quotes.Add(quote);

        return new QuoteDto(quote.Id, quote.Provider, quote.DistanceM, quote.DurationS, quote.Route, quote.FareVnd);
    }

    public ApiResult CreateRide(Guid riderId, Guid quoteId, DateTimeOffset now)
    {
        lock (_gate)
        {
            var quote = _quotes.FirstOrDefault(item => item.Id == quoteId);
            if (quote is null)
                return new ApiResult.Empty(StatusCodes.Status404NotFound);

            foreach (var existing in _rides)
            {
                if (existing.RiderId == riderId && IsActive(existing.Status))
                    return new ApiResult.ActiveRideConflict(existing.Id);
            }

            var ride = new Ride
            {
                Id = Guid.NewGuid(),
                RiderId = riderId,
                QuoteId = quote.Id,
                Status = RideStatus.Searching,
                Pickup = quote.Pickup,
                Dropoff = quote.Dropoff,
                DistanceM = quote.DistanceM,
                DurationS = quote.DurationS,
                FareVnd = quote.FareVnd,
                Route = quote.Route,
                Round = 1,
            };
            _rides.Add(ride);

            var result = new ApiResult.JsonBody<CreatedRideDto>(
                StatusCodes.Status201Created,
                new CreatedRideDto(ride.Id));
            AdvanceDispatch(ride, now, result.Events);
            return result;
        }
    }

    public RideView? GetRide(Guid id)
    {
        lock (_gate)
        {
            var ride = FindRide(id);
            return ride is null ? null : ToView(ride);
        }
    }

    public RideView? GetCurrent(Guid userId)
    {
        lock (_gate)
        {
            var user = _users.FirstOrDefault(item => item.Id == userId);
            if (user is null)
                return null;

            if (user.Role == UserRole.Driver)
            {
                var driver = FindDriver(userId);
                if (driver?.CurrentRideId is not Guid rideId)
                    return null;
                var assigned = FindRide(rideId);
                return assigned is not null && IsActive(assigned.Status) ? ToView(assigned) : null;
            }

            Ride? active = null;
            Ride? terminal = null;
            foreach (var ride in _rides)
            {
                if (ride.RiderId != userId)
                    continue;
                if (IsActive(ride.Status))
                    active = ride;
                else if (ride.Status is RideStatus.NoDriver or RideStatus.Cancelled or RideStatus.Completed)
                    terminal = ride;
            }

            var latest = active ?? terminal;
            return latest is null ? null : ToView(latest);
        }
    }

    public IReadOnlyList<OfferDto> PendingOffers(Guid driverId, DateTimeOffset now)
    {
        lock (_gate)
        {
            var offers = new List<OfferDto>();
            foreach (var offer in _offers)
            {
                if (offer.DriverId != driverId || offer.Status != OfferStatus.Pending || offer.ExpiresAt <= now)
                    continue;

                var ride = FindRide(offer.RideId);
                if (ride is null || ride.Status != RideStatus.Searching)
                    continue;

                offers.Add(ToOfferDto(offer, ride));
            }

            return offers;
        }
    }

    public ApiResult Accept(Guid driverId, Guid offerId, DateTimeOffset now)
    {
        lock (_gate)
        {
            var offer = FindOffer(offerId);
            if (offer is null)
                return new ApiResult.Empty(StatusCodes.Status404NotFound);

            var driver = FindDriver(driverId);
            if (driver is null || offer.DriverId != driverId)
                return new ApiResult.Empty(StatusCodes.Status403Forbidden);

            var ride = FindRide(offer.RideId);
            if (ride is null)
                return new ApiResult.Empty(StatusCodes.Status404NotFound);

            // Ride state first so a losing accept is Taken, then Busy, then Closed.
            if (ride.Status != RideStatus.Searching)
                return new ApiResult.AcceptConflict("Taken");
            if (driver.CurrentRideId is not null || driver.Status == DriverStatus.OnTrip)
                return new ApiResult.AcceptConflict("Busy");
            if (offer.Status != OfferStatus.Pending || offer.ExpiresAt <= now)
                return new ApiResult.AcceptConflict("Closed");

            ride.Status = RideStatus.Accepted;
            ride.DriverId = driver.UserId;
            driver.Status = DriverStatus.OnTrip;
            driver.CurrentRideId = ride.Id;
            driver.SpeedKmh = 0;
            offer.Status = OfferStatus.Accepted;

            var view = ToView(ride);
            var result = new ApiResult.JsonBody<RideView>(StatusCodes.Status200OK, view);
            result.Events.Add(new OutboundEvent.StatusChanged(view, ride.RiderId, driver.UserId));

            foreach (var other in _offers)
            {
                if (other.RideId != ride.Id || other.Id == offer.Id || other.Status != OfferStatus.Pending)
                    continue;

                other.Status = OfferStatus.Closed;
                result.Events.Add(new OutboundEvent.OfferClosed(other.DriverId, other.Id, "Taken"));
            }

            return result;
        }
    }

    public ApiResult Decline(Guid driverId, Guid offerId, DateTimeOffset now)
    {
        lock (_gate)
        {
            var offer = FindOffer(offerId);
            if (offer is null)
                return new ApiResult.Empty(StatusCodes.Status404NotFound);
            if (offer.DriverId != driverId)
                return new ApiResult.Empty(StatusCodes.Status403Forbidden);

            var result = new ApiResult.Empty(StatusCodes.Status200OK);
            if (offer.Status == OfferStatus.Pending)
            {
                offer.Status = OfferStatus.Declined;
                var ride = FindRide(offer.RideId);
                if (ride is not null)
                    AdvanceDispatch(ride, now, result.Events);
            }

            return result;
        }
    }

    public ApiResult Arrived(Guid driverId, Guid rideId) =>
        Transition(driverId, rideId, asDriver: true, RideStatus.Accepted, RideStatus.DriverArrived);

    public ApiResult Start(Guid driverId, Guid rideId) =>
        Transition(driverId, rideId, asDriver: true, RideStatus.DriverArrived, RideStatus.InProgress);

    public ApiResult Dropoff(Guid driverId, Guid rideId) =>
        Transition(driverId, rideId, asDriver: true, RideStatus.InProgress, RideStatus.DroppedOff);

    public ApiResult ConfirmArrival(Guid riderId, Guid rideId)
    {
        lock (_gate)
        {
            var ride = FindRide(rideId);
            if (ride is null)
                return new ApiResult.Empty(StatusCodes.Status404NotFound);
            if (ride.RiderId != riderId)
                return new ApiResult.Empty(StatusCodes.Status403Forbidden);
            if (ride.Status != RideStatus.DroppedOff)
                return TransitionConflict(ride.Status, RideStatus.Completed);

            ride.Status = RideStatus.Completed;
            ReleaseDriver(ride);
            return StatusResult(ride);
        }
    }

    public ApiResult Cancel(Guid riderId, Guid rideId)
    {
        lock (_gate)
        {
            var ride = FindRide(rideId);
            if (ride is null)
                return new ApiResult.Empty(StatusCodes.Status404NotFound);
            if (ride.RiderId != riderId)
                return new ApiResult.Empty(StatusCodes.Status403Forbidden);
            if (ride.Status is not (RideStatus.Searching or RideStatus.Accepted or RideStatus.DriverArrived))
                return TransitionConflict(ride.Status, RideStatus.Cancelled);

            ride.Status = RideStatus.Cancelled;
            ReleaseDriver(ride);
            var result = StatusResult(ride);
            foreach (var offer in _offers)
            {
                if (offer.RideId != ride.Id || offer.Status != OfferStatus.Pending)
                    continue;

                offer.Status = OfferStatus.Closed;
                result.Events.Add(new OutboundEvent.OfferClosed(offer.DriverId, offer.Id, "Cancelled"));
            }

            return result;
        }
    }

    public List<OutboundEvent> Tick(DateTimeOffset now, double stepMeters)
    {
        lock (_gate)
        {
            var events = new List<OutboundEvent>();
            foreach (var driver in _drivers)
            {
                if (driver.Status != DriverStatus.OnTrip || driver.CurrentRideId is not Guid rideId)
                    continue;

                var ride = FindRide(rideId);
                if (ride is null)
                    continue;

                if (ride.Status == RideStatus.Accepted)
                {
                    var moved = Geo.MoveToward(driver.Lat, driver.Lng, ride.Pickup.Lat, ride.Pickup.Lng, stepMeters);
                    ApplyMovement(driver, moved.Lat, moved.Lng, moved.Heading, moved.MovedM);
                    if (moved.MovedM >= 0.5)
                        events.Add(LocationEvent(ride, driver, now));
                }
                else if (ride.Status == RideStatus.InProgress)
                {
                    var moved = Geo.AdvanceRoute(ride.Route.Coordinates, ride.SegmentIndex, ride.SegmentOffsetM, stepMeters);
                    ride.SegmentIndex = moved.Segment;
                    ride.SegmentOffsetM = moved.OffsetM;
                    ApplyMovement(driver, moved.Lat, moved.Lng, moved.Heading, moved.MovedM);
                    if (moved.MovedM >= 0.5)
                        events.Add(LocationEvent(ride, driver, now));
                }
            }

            foreach (var offer in _offers)
            {
                if (offer.Status != OfferStatus.Pending || offer.ExpiresAt > now)
                    continue;

                offer.Status = OfferStatus.Closed;
                events.Add(new OutboundEvent.OfferClosed(offer.DriverId, offer.Id, "Expired"));
            }

            foreach (var ride in _rides)
                AdvanceDispatch(ride, now, events);

            return events;
        }
    }

    private ApiResult Transition(Guid actorId, Guid rideId, bool asDriver, RideStatus required, RideStatus target)
    {
        lock (_gate)
        {
            var ride = FindRide(rideId);
            if (ride is null)
                return new ApiResult.Empty(StatusCodes.Status404NotFound);

            if (asDriver)
            {
                if (ride.DriverId != actorId)
                    return new ApiResult.Empty(StatusCodes.Status403Forbidden);
            }
            else if (ride.RiderId != actorId)
            {
                return new ApiResult.Empty(StatusCodes.Status403Forbidden);
            }

            if (ride.Status != required)
                return TransitionConflict(ride.Status, target);

            ride.Status = target;
            if (target == RideStatus.DriverArrived && ride.DriverId is Guid arrivedDriverId)
            {
                var arrivedDriver = FindDriver(arrivedDriverId);
                if (arrivedDriver is not null)
                    arrivedDriver.SpeedKmh = 0;
            }

            if (target == RideStatus.InProgress)
                SnapToRouteStart(ride);

            return StatusResult(ride);
        }
    }

    private static ApiResult TransitionConflict(RideStatus from, RideStatus to) =>
        new ApiResult.TransitionConflict(from.ToString(), to.ToString());

    private ApiResult.JsonBody<RideView> StatusResult(Ride ride)
    {
        var view = ToView(ride);
        var result = new ApiResult.JsonBody<RideView>(StatusCodes.Status200OK, view);
        result.Events.Add(new OutboundEvent.StatusChanged(view, ride.RiderId, ride.DriverId));
        return result;
    }

    private void SnapToRouteStart(Ride ride)
    {
        ride.SegmentIndex = 0;
        ride.SegmentOffsetM = 0;
        if (ride.Route.Coordinates.Length == 0 || ride.DriverId is not Guid driverId)
            return;

        var driver = FindDriver(driverId);
        if (driver is null)
            return;

        var start = ride.Route.Coordinates[0];
        driver.Lng = start[0];
        driver.Lat = start[1];
        driver.SpeedKmh = 0;
    }

    private void ReleaseDriver(Ride ride)
    {
        if (ride.DriverId is not Guid driverId)
            return;

        var driver = FindDriver(driverId);
        if (driver is null || driver.CurrentRideId != ride.Id)
            return;

        driver.CurrentRideId = null;
        driver.Status = DriverStatus.Online;
        driver.SpeedKmh = 0;
    }

    private void AdvanceDispatch(Ride ride, DateTimeOffset now, List<OutboundEvent> events)
    {
        if (ride.Status != RideStatus.Searching)
            return;
        if (_offers.Any(offer => offer.RideId == ride.Id && offer.Status == OfferStatus.Pending && offer.ExpiresAt > now))
            return;

        var offered = new HashSet<Guid>();
        var hadOffers = false;
        foreach (var offer in _offers)
        {
            if (offer.RideId != ride.Id)
                continue;
            hadOffers = true;
            offered.Add(offer.DriverId);
        }

        if (hadOffers && ride.Round >= MaxRounds)
        {
            MarkNoDriver(ride, events);
            return;
        }

        var ranked = RankDrivers(ride.Pickup.Lat, ride.Pickup.Lng, offered);
        if (ranked.Count == 0)
        {
            MarkNoDriver(ride, events);
            return;
        }

        if (hadOffers)
            ride.Round++;

        var expiresAt = now.AddSeconds(OfferTtlSeconds);
        var take = Math.Min(MaxOffers, ranked.Count);
        for (var i = 0; i < take; i++)
        {
            var offer = new Offer
            {
                Id = Guid.NewGuid(),
                RideId = ride.Id,
                DriverId = ranked[i].Driver.UserId,
                Round = ride.Round,
                Status = OfferStatus.Pending,
                ExpiresAt = expiresAt,
            };
            _offers.Add(offer);
            events.Add(new OutboundEvent.OfferReceived(offer.DriverId, ToOfferDto(offer, ride)));
        }

        events.Add(new OutboundEvent.StatusChanged(ToView(ride), ride.RiderId, null));
    }

    private void MarkNoDriver(Ride ride, List<OutboundEvent> events)
    {
        ride.Status = RideStatus.NoDriver;
        events.Add(new OutboundEvent.StatusChanged(ToView(ride), ride.RiderId, null));
    }

    private List<(Driver Driver, double Distance)> RankDrivers(double lat, double lng, HashSet<Guid> exclude)
    {
        var ranked = new List<(Driver Driver, double Distance)>();
        foreach (var driver in _drivers)
        {
            if (exclude.Contains(driver.UserId))
                continue;
            if (driver.Status != DriverStatus.Online || driver.CurrentRideId is not null)
                continue;

            var distance = Geo.HaversineMeters(lat, lng, driver.Lat, driver.Lng);
            if (distance <= DispatchRadiusM)
                ranked.Add((driver, distance));
        }

        ranked.Sort((left, right) =>
        {
            var compared = left.Distance.CompareTo(right.Distance);
            return compared != 0
                ? compared
                : left.Driver.UserId.CompareTo(right.Driver.UserId);
        });
        return ranked;
    }

    private static void ApplyMovement(Driver driver, double lat, double lng, double heading, double movedM)
    {
        driver.Lat = lat;
        driver.Lng = lng;
        if (movedM >= 0.5)
        {
            driver.Heading = heading;
            driver.SpeedKmh = movedM * 3.6;
        }
        else
        {
            driver.SpeedKmh = 0;
        }
    }

    private static OutboundEvent LocationEvent(Ride ride, Driver driver, DateTimeOffset now) =>
        new OutboundEvent.Location(
            ride.RiderId,
            driver.UserId,
            new DriverLocationDto(ride.Id, driver.UserId, driver.Lat, driver.Lng, driver.Heading, driver.SpeedKmh, now));

    private RideView ToView(Ride ride)
    {
        DriverSnapshot? driver = null;
        if (ride.DriverId is Guid driverId)
        {
            var assigned = FindDriver(driverId);
            if (assigned is not null)
                driver = new DriverSnapshot(assigned.UserId, assigned.Name, assigned.Plate, assigned.Lat, assigned.Lng);
        }

        return new RideView(
            ride.Id,
            ride.Status.ToString(),
            ride.Pickup,
            ride.Dropoff,
            ride.DistanceM,
            ride.DurationS,
            ride.FareVnd,
            ride.Route,
            ride.Round,
            driver);
    }

    private OfferDto ToOfferDto(Offer offer, Ride ride)
    {
        var driver = FindDriver(offer.DriverId);
        var pickupDistance = driver is null
            ? 0
            : (int)Math.Round(
                Geo.HaversineMeters(driver.Lat, driver.Lng, ride.Pickup.Lat, ride.Pickup.Lng),
                MidpointRounding.AwayFromZero);
        return new OfferDto(
            offer.Id,
            ride.Id,
            ride.Pickup,
            ride.Dropoff,
            ride.DistanceM,
            ride.DurationS,
            offer.ExpiresAt,
            offer.Round,
            pickupDistance);
    }

    private static PlaceDto ToPlaceDto(Place place) =>
        new(place.Id, place.Name, place.Address, place.Lat, place.Lng);

    private static bool Matches(Place place, string query)
    {
        var label = $"{place.Name}, {place.Address}";
        return place.Name.Contains(query, StringComparison.OrdinalIgnoreCase)
            || place.Address.Contains(query, StringComparison.OrdinalIgnoreCase)
            || label.Contains(query, StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsActive(RideStatus status) => status is
        RideStatus.Searching or
        RideStatus.Accepted or
        RideStatus.DriverArrived or
        RideStatus.InProgress or
        RideStatus.DroppedOff;

    private Driver? FindDriver(Guid id) => _drivers.FirstOrDefault(driver => driver.UserId == id);
    private Ride? FindRide(Guid id) => _rides.FirstOrDefault(ride => ride.Id == id);
    private Offer? FindOffer(Guid id) => _offers.FirstOrDefault(offer => offer.Id == id);

    private void Seed()
    {
        AddRider("11111111-1111-1111-1111-111111111111", "An Nguyễn", "HCMC");
        AddRider("22222222-2222-2222-2222-222222222222", "Mai Trần", "HCMC");
        AddDriver("33333333-3333-3333-3333-333333333333", "Khoa Phạm", "59-H1 234.56", "HCMC", 10.7735, 106.6990);
        AddDriver("44444444-4444-4444-4444-444444444444", "Linh Võ", "59-P2 118.20", "HCMC", 10.7705, 106.6965);
        AddDriver("55555555-5555-5555-5555-555555555555", "Đức Lê", "59-H1 908.11", "HCMC", 10.7740, 106.7010);
        AddDriver("66666666-6666-6666-6666-666666666666", "Hà Bùi", "59-N1 441.07", "HCMC", 10.7690, 106.7000);
        AddRider("77777777-7777-7777-7777-777777777777", "Nam Đỗ", "Hanoi");
        AddRider("88888888-8888-8888-8888-888888888888", "Hoa Lý", "Hanoi");
        // Exact Hanoi coordinates were not specified; these sit within a few hundred meters of Hoàn Kiếm.
        AddDriver("99999999-9999-9999-9999-999999999999", "Minh Trần", "29-H1 100.01", "Hanoi", 21.02980, 105.85360);
        AddDriver("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa", "Quân Phạm", "29-P1 200.02", "Hanoi", 21.02770, 105.85110);
        AddDriver("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb", "Lan Ngô", "29-H1 300.03", "Hanoi", 21.03050, 105.85080);
        AddDriver("cccccccc-cccc-cccc-cccc-cccccccccccc", "Sơn Vũ", "29-N1 400.04", "Hanoi", 21.02680, 105.85430);

        _places.Add(new Place
        {
            Id = "place-ben-thanh",
            Name = "Chợ Bến Thành",
            Address = "District 1",
            Lat = 10.77254,
            Lng = 106.69800,
        });
        _places.Add(new Place
        {
            Id = "place-notre-dame",
            Name = "Nhà thờ Đức Bà",
            Address = "District 1",
            Lat = 10.77979,
            Lng = 106.69902,
        });
        _places.Add(new Place
        {
            Id = "place-bitexco",
            Name = "Bitexco Financial Tower",
            Address = "2 Hai Trieu, District 1",
            Lat = 10.77175,
            Lng = 106.70446,
        });
        _places.Add(new Place
        {
            Id = "place-hoan-kiem",
            Name = "Hồ Hoàn Kiếm",
            Address = "Hanoi",
            Lat = 21.02870,
            Lng = 105.85240,
        });
    }

    private void AddRider(string id, string name, string city)
    {
        _users.Add(new User
        {
            Id = Guid.Parse(id),
            Name = name,
            Role = UserRole.Rider,
            City = city,
        });
    }

    private void AddDriver(string id, string name, string plate, string city, double lat, double lng)
    {
        var userId = Guid.Parse(id);
        _users.Add(new User
        {
            Id = userId,
            Name = name,
            Role = UserRole.Driver,
            City = city,
        });
        _drivers.Add(new Driver
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

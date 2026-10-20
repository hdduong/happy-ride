import type {
  DevUser,
  DriverLocation,
  GeoLine,
  Offer,
  OfferClosed,
  Place,
  PlacePoint,
  Quote,
  RideDriver,
  RideView,
} from "@/lib/types";

function record(value: unknown): Record<string, unknown> | null {
  if (!value || typeof value !== "object" || Array.isArray(value)) return null;
  return value as Record<string, unknown>;
}

function str(value: Record<string, unknown>, ...keys: string[]): string | null {
  for (const key of keys) {
    const item = value[key];
    if (typeof item === "string" && item.trim()) return item;
  }
  return null;
}

function num(value: Record<string, unknown>, ...keys: string[]): number | null {
  for (const key of keys) {
    const item = value[key];
    if (typeof item === "number" && Number.isFinite(item)) return item;
    if (typeof item === "string" && item.trim() && Number.isFinite(Number(item))) {
      return Number(item);
    }
  }
  return null;
}

export function asPoint(value: unknown): PlacePoint | null {
  const row = record(value);
  if (!row) return null;
  const lat = num(row, "lat", "Lat");
  const lng = num(row, "lng", "Lng", "lon", "Lon");
  if (lat === null || lng === null) return null;
  const address = str(row, "address", "Address", "name", "Name") ?? "";
  return { lat, lng, address };
}

export function asPlace(value: unknown): Place | null {
  const row = record(value);
  const point = asPoint(value);
  if (!row || !point) return null;
  const id = str(row, "id", "Id");
  const name = str(row, "name", "Name") ?? point.address;
  if (!id || !name) return null;
  return {
    id,
    name,
    address: str(row, "address", "Address") ?? name,
    lat: point.lat,
    lng: point.lng,
  };
}

export function asUsers(value: unknown): DevUser[] {
  if (!Array.isArray(value)) throw new Error("User list was not an array");
  return value.flatMap((item) => {
    const row = record(item);
    if (!row) return [];
    const id = str(row, "id", "Id");
    const name = str(row, "name", "Name");
    const role = str(row, "role", "Role");
    const city = str(row, "city", "City");
    if (!id || !name || !role || !city) return [];
    return [{ id, name, role, city, plate: str(row, "plate", "Plate") }];
  });
}

export function asRoute(value: unknown): GeoLine | null {
  const row = record(value);
  if (!row) return null;
  const coordinates = row.coordinates ?? row.Coordinates;
  if (!Array.isArray(coordinates)) return null;
  const pairs: number[][] = [];
  for (const pair of coordinates) {
    if (!Array.isArray(pair) || pair.length < 2) continue;
    const lng = Number(pair[0]);
    const lat = Number(pair[1]);
    if (Number.isFinite(lat) && Number.isFinite(lng)) pairs.push([lng, lat]);
  }
  return { type: "LineString", coordinates: pairs };
}

export function asQuote(value: unknown): Quote | null {
  const row = record(value);
  if (!row) return null;
  const quoteId = str(row, "quoteId", "QuoteId");
  const distanceM = num(row, "distanceM", "DistanceM");
  const durationS = num(row, "durationS", "DurationS");
  const fareVnd = num(row, "fareVnd", "FareVnd");
  const route = asRoute(row.route ?? row.Route);
  if (!quoteId || distanceM === null || durationS === null || fareVnd === null || !route) {
    return null;
  }
  return {
    quoteId,
    provider: str(row, "provider", "Provider") ?? "Fake",
    distanceM,
    durationS,
    fareVnd,
    route,
  };
}

function asDriver(value: unknown): RideDriver | null {
  const row = record(value);
  if (!row) return null;
  const userId = str(row, "userId", "UserId");
  const name = str(row, "name", "Name");
  const plate = str(row, "plate", "Plate");
  const lat = num(row, "lat", "Lat");
  const lng = num(row, "lng", "Lng");
  if (!userId || !name || !plate || lat === null || lng === null) return null;
  return { userId, name, plate, lat, lng };
}

export function asRide(value: unknown): RideView | null {
  const row = record(value);
  if (!row) return null;
  const rideId = str(row, "rideId", "RideId");
  const status = str(row, "status", "Status");
  const pickup = asPoint(row.pickup ?? row.Pickup);
  const dropoff = asPoint(row.dropoff ?? row.Dropoff);
  if (!rideId || !status || !pickup || !dropoff) return null;
  const driverValue = row.driver === undefined ? row.Driver : row.driver;
  return {
    rideId,
    status,
    pickup,
    dropoff,
    distanceM: num(row, "distanceM", "DistanceM") ?? 0,
    durationS: num(row, "durationS", "DurationS") ?? 0,
    fareVnd: num(row, "fareVnd", "FareVnd") ?? 0,
    route: asRoute(row.route ?? row.Route) ?? { type: "LineString", coordinates: [] },
    driver: driverValue == null ? null : asDriver(driverValue),
  };
}

export function asRideId(value: unknown): string | null {
  const row = record(value);
  if (!row) return null;
  return str(row, "rideId", "RideId");
}

export function asOffers(value: unknown): Offer[] {
  if (!Array.isArray(value)) throw new Error("Offer list was not an array");
  return value.flatMap((item) => {
    const offer = asOffer(item);
    return offer ? [offer] : [];
  });
}

export function asOffer(value: unknown): Offer | null {
  const row = record(value);
  if (!row) return null;
  const offerId = str(row, "offerId", "OfferId") ?? str(row, "id", "Id");
  const rideId = str(row, "rideId", "RideId");
  if (!offerId || !rideId) return null;
  return {
    offerId,
    rideId,
    pickup: asPoint(row.pickup ?? row.Pickup) ?? { lat: 0, lng: 0, address: "" },
    dropoff: asPoint(row.dropoff ?? row.Dropoff) ?? { lat: 0, lng: 0, address: "" },
    distanceM: num(row, "distanceM", "DistanceM") ?? 0,
    durationS: num(row, "durationS", "DurationS") ?? 0,
    expiresAt: str(row, "expiresAt", "ExpiresAt") ?? "",
    round: num(row, "round", "Round") ?? 1,
  };
}

export function asLocation(value: unknown): DriverLocation | null {
  const row = record(value);
  if (!row) return null;
  const rideId = str(row, "rideId", "RideId");
  const lat = num(row, "lat", "Lat");
  const lng = num(row, "lng", "Lng");
  if (!rideId || lat === null || lng === null) return null;
  return {
    rideId,
    driverId: str(row, "driverId", "DriverId") ?? "",
    lat,
    lng,
    heading: num(row, "heading", "Heading"),
    speedKmh: num(row, "speedKmh", "SpeedKmh"),
    at: str(row, "at", "At"),
  };
}

export function asOfferClosed(value: unknown): OfferClosed | null {
  const row = record(value);
  if (!row) return null;
  const offerId = str(row, "offerId", "OfferId");
  if (!offerId) return null;
  return { offerId, reason: str(row, "reason", "Reason") ?? "" };
}

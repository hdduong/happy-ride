import { record, str, num } from "@/shared/api/parse";
import { asPoint, asRoute } from "@/features/maps/parse";
import type { RideDriver, RideView, Offer, DriverLocation, OfferClosed } from "./types";

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
    approachRoute: asRoute(row.approachRoute ?? row.ApproachRoute),
    round: num(row, "round", "Round") ?? 1,
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
    pickupDistanceM: num(row, "pickupDistanceM", "PickupDistanceM") ?? 0,
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

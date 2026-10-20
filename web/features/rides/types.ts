import type { GeoLine, PlacePoint } from "@/features/maps/types";

export type RideDriver = {
  userId: string;
  name: string;
  plate: string;
  lat: number;
  lng: number;
};

export type RideView = {
  rideId: string;
  status: string;
  pickup: PlacePoint;
  dropoff: PlacePoint;
  distanceM: number;
  durationS: number;
  fareVnd: number;
  route: GeoLine;
  round: number;
  driver: RideDriver | null;
};

export type Offer = {
  offerId: string;
  rideId: string;
  pickup: PlacePoint;
  dropoff: PlacePoint;
  distanceM: number;
  durationS: number;
  expiresAt: string;
  round: number;
  pickupDistanceM: number;
};

export type DriverLocation = {
  rideId: string;
  driverId: string;
  lat: number;
  lng: number;
  heading: number | null;
  speedKmh: number | null;
  at: string | null;
};

export type OfferClosed = {
  offerId: string;
  reason: string;
};

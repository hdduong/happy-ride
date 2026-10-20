export type LatLng = {
  lat: number;
  lng: number;
};

export type PlacePoint = LatLng & {
  address: string;
};

export type Place = {
  id: string;
  name: string;
  address: string;
  lat: number;
  lng: number;
};

export type DevUser = {
  id: string;
  name: string;
  role: string;
  city: string;
  plate: string | null;
};

export type GeoLine = {
  type: "LineString";
  coordinates: number[][];
};

export type Quote = {
  quoteId: string;
  provider: string;
  distanceM: number;
  durationS: number;
  fareVnd: number;
  route: GeoLine;
};

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

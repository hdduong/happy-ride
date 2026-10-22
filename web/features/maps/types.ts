export type LatLng = {
  lat: number;
  lng: number;
};

export type MapMarker = LatLng & {
  kind: "pickup" | "dropoff" | "bike";
  heading?: number;
  offset?: [number, number];
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

export type GeoLine = {
  type: "LineString";
  coordinates: number[][];
};

export type MapClientConfig = {
  provider: string;
  styleUrl: string | null;
  browserKey: string | null;
  region: string;
  language: string;
};

export type Quote = {
  quoteId: string;
  provider: string;
  distanceM: number;
  durationS: number;
  fareVnd: number;
  route: GeoLine;
};

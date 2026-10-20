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

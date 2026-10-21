import { record, str, num } from "@/shared/api/parse";
import type { GeoLine, MapClientConfig, Place, PlacePoint, Quote } from "./types";

export function asMapConfig(value: unknown): MapClientConfig | null {
  const row = record(value);
  if (!row) return null;
  const provider = str(row, "provider", "Provider");
  if (!provider) return null;
  return {
    provider,
    styleUrl: str(row, "styleUrl", "StyleUrl"),
    browserKey: str(row, "browserKey", "BrowserKey"),
    region: str(row, "region", "Region") ?? "VN",
    language: str(row, "language", "Language") ?? "vi",
  };
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

import { request } from "@/shared/api/client";
import { asMapConfig, asPlace, asQuote } from "./parse";
import type { MapClientConfig, Place, PlacePoint, Quote } from "./types";

export async function getMapConfig(): Promise<MapClientConfig> {
  const payload = await request<unknown>("/api/map-config");
  const config = asMapConfig(payload);
  if (!config) throw new Error("Map config was incomplete");
  return config;
}

export function autocomplete(userId: string, query: string, lat: number, lng: number): Promise<Place[]> {
  const params = new URLSearchParams({
    q: query,
    lat: String(lat),
    lng: String(lng),
  });
  return request<unknown>(`/api/places/autocomplete?${params.toString()}`, { userId }).then((payload) => {
    if (!Array.isArray(payload)) throw new Error("Place search response was not a list");
    return payload.flatMap((item) => {
      const place = asPlace(item);
      return place ? [place] : [];
    });
  });
}

export async function getPlace(userId: string, placeId: string): Promise<Place> {
  const payload = await request<unknown>(`/api/places/${encodeURIComponent(placeId)}`, { userId });
  const place = asPlace(payload);
  if (!place) throw new Error("Place response was incomplete");
  return place;
}

export async function createQuote(
  userId: string,
  pickup: PlacePoint,
  dropoff: PlacePoint,
): Promise<Quote> {
  const payload = await request<unknown>("/api/quotes", {
    method: "POST",
    userId,
    body: { pickup, dropoff },
  });
  const quote = asQuote(payload);
  if (!quote) throw new Error("Quote response was incomplete");
  return quote;
}

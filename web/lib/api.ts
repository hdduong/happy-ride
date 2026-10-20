import {
  asOffers,
  asPlace,
  asQuote,
  asRide,
  asRideId,
  asUsers,
} from "@/lib/parse";
import type { DevUser, Offer, Place, PlacePoint, Quote, RideView } from "@/lib/types";

export function apiBase(): string {
  const configured = process.env.NEXT_PUBLIC_API_BASE_URL?.trim();
  const base = configured && configured.length > 0 ? configured : "http://localhost:5080";
  return base.replace(/\/+$/, "");
}

export class ApiError extends Error {
  readonly status: number;
  readonly payload: unknown;

  constructor(status: number, payload: unknown) {
    super(readApiMessage(payload) ?? `Request failed (${status})`);
    this.name = "ApiError";
    this.status = status;
    this.payload = payload;
  }
}

export function readApiMessage(payload: unknown): string | null {
  if (typeof payload === "string" && payload.trim()) return payload.trim();
  if (!payload || typeof payload !== "object") return null;
  const body = payload as Record<string, unknown>;
  if (typeof body.reason === "string" && body.reason.trim()) return body.reason.trim();
  if (typeof body.error === "string" && body.error.trim()) return body.error.trim();
  return null;
}

export function messageFrom(error: unknown): string {
  if (error instanceof ApiError) return error.message;
  if (error instanceof TypeError) return "Can't reach the server";
  if (error instanceof Error && error.message) return error.message;
  return "Something went wrong";
}

type RequestOptions = {
  method?: string;
  userId?: string | null;
  body?: unknown;
};

async function request<T>(path: string, options: RequestOptions = {}): Promise<T> {
  const headers = new Headers();
  if (options.userId) headers.set("X-User-Id", options.userId);
  let body: string | undefined;
  if (options.body !== undefined) {
    headers.set("Content-Type", "application/json");
    body = JSON.stringify(options.body);
  }

  const response = await fetch(`${apiBase()}${path}`, {
    method: options.method ?? "GET",
    headers,
    body,
    cache: "no-store",
  });

  if (response.status === 204 || response.status === 205) return null as T;
  const text = await response.text();
  let payload: unknown = null;
  if (text) {
    try {
      payload = JSON.parse(text) as unknown;
    } catch {
      payload = { error: text };
    }
  }
  if (!response.ok) throw new ApiError(response.status, payload);
  return payload as T;
}

function requireRide(value: unknown, label: string): RideView {
  const ride = asRide(value);
  if (!ride) throw new Error(`${label} was incomplete`);
  return ride;
}

export function getUsers(userId?: string | null): Promise<DevUser[]> {
  return request<unknown>("/api/dev/users", { userId }).then((payload) => asUsers(payload));
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

export async function createRide(userId: string, quoteId: string): Promise<string> {
  const payload = await request<unknown>("/api/rides", {
    method: "POST",
    userId,
    body: { quoteId },
  });
  const rideId = asRideId(payload);
  if (!rideId) throw new Error("Ride response was incomplete");
  return rideId;
}

export async function getRide(userId: string, rideId: string): Promise<RideView> {
  const payload = await request<unknown>(`/api/rides/${encodeURIComponent(rideId)}`, { userId });
  return requireRide(payload, "Ride response");
}

export async function getCurrentRide(userId: string): Promise<RideView | null> {
  const payload = await request<unknown>("/api/rides/current", { userId });
  if (payload == null) return null;
  return requireRide(payload, "Ride response");
}

async function postRide(userId: string, rideId: string, action: string): Promise<RideView | null> {
  const payload = await request<unknown>(`/api/rides/${encodeURIComponent(rideId)}/${action}`, {
    method: "POST",
    userId,
  });
  if (payload == null) return getCurrentRide(userId);
  const ride = asRide(payload);
  if (ride) return ride;
  return getCurrentRide(userId);
}

export function cancelRide(userId: string, rideId: string): Promise<RideView | null> {
  return postRide(userId, rideId, "cancel");
}

export function confirmArrival(userId: string, rideId: string): Promise<RideView | null> {
  return postRide(userId, rideId, "confirm-arrival");
}

export function rideAction(
  userId: string,
  rideId: string,
  action: "arrived" | "start" | "dropoff",
): Promise<RideView | null> {
  return postRide(userId, rideId, action);
}

export function getPendingOffers(userId: string): Promise<Offer[]> {
  return request<unknown>("/api/offers/pending", { userId }).then((payload) => asOffers(payload));
}

export async function acceptOffer(userId: string, offerId: string): Promise<RideView> {
  const payload = await request<unknown>(`/api/offers/${encodeURIComponent(offerId)}/accept`, {
    method: "POST",
    userId,
  });
  const ride = asRide(payload);
  if (ride) return ride;
  const current = await getCurrentRide(userId);
  if (!current) throw new Error("Accept did not return a ride");
  return current;
}

export function declineOffer(userId: string, offerId: string): Promise<void> {
  return request<unknown>(`/api/offers/${encodeURIComponent(offerId)}/decline`, {
    method: "POST",
    userId,
  }).then(() => undefined);
}

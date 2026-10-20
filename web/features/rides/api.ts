import { request } from "@/shared/api/client";
import { asOffers, asRide, asRideId } from "./parse";
import type { RideView, Offer } from "./types";

function requireRide(value: unknown, label: string): RideView {
  const ride = asRide(value);
  if (!ride) throw new Error(`${label} was incomplete`);
  return ride;
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

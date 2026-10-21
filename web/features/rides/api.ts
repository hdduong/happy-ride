import { request } from "@/shared/api/client";
import { asOffers, asRide, asRideId } from "./parse";
import type { RideView, Offer } from "./types";
import { getActivityClient } from "@/shared/telemetry/browser";
import { recordAction, type ActivityAction, type Correlation } from "@/shared/telemetry/activity";

function requireRide(value: unknown, label: string): RideView {
  const ride = asRide(value);
  if (!ride) throw new Error(`${label} was incomplete`);
  return ride;
}

export function createRide(userId: string, quoteId: string): Promise<string> {
  return recordAction(getActivityClient(userId), "Book", {}, async (telemetry) => {
    const payload = await request<unknown>("/api/rides", {
      method: "POST", userId, body: { quoteId }, telemetry,
    });
    const rideId = asRideId(payload);
    if (!rideId) throw new Error("Ride response was incomplete");
    return rideId;
  }, (rideId) => rideId);
}

export async function getRide(userId: string, rideId: string): Promise<RideView> {
  const telemetry = getActivityClient(userId)?.correlation();
  const payload = await request<unknown>(`/api/rides/${encodeURIComponent(rideId)}`, {
    userId, telemetry: telemetry ? { ...telemetry, rideId } : undefined,
  });
  return requireRide(payload, "Ride response");
}

export async function getCurrentRide(userId: string): Promise<RideView | null> {
  const payload = await request<unknown>("/api/rides/current", { userId });
  if (payload == null) return null;
  return requireRide(payload, "Ride response");
}

async function postRide(userId: string, rideId: string, action: string, telemetry?: Correlation): Promise<RideView | null> {
  const payload = await request<unknown>(`/api/rides/${encodeURIComponent(rideId)}/${action}`, {
    method: "POST",
    userId,
    telemetry,
  });
  if (payload == null) return getCurrentRide(userId);
  const ride = asRide(payload);
  if (ride) return ride;
  return getCurrentRide(userId);
}

export function cancelRide(userId: string, rideId: string): Promise<RideView | null> {
  return recordAction(getActivityClient(userId), "Cancel", { rideId }, (telemetry) =>
    postRide(userId, rideId, "cancel", telemetry));
}

export function confirmArrival(userId: string, rideId: string): Promise<RideView | null> {
  return recordAction(getActivityClient(userId), "ConfirmArrival", { rideId }, (telemetry) =>
    postRide(userId, rideId, "confirm-arrival", telemetry));
}

export function rideAction(
  userId: string,
  rideId: string,
  action: "arrived" | "start" | "dropoff",
): Promise<RideView | null> {
  const event: ActivityAction = action === "arrived" ? "Arrived" : action === "start" ? "Start" : "Dropoff";
  return recordAction(getActivityClient(userId), event, { rideId }, (telemetry) =>
    postRide(userId, rideId, action, telemetry));
}

export function getPendingOffers(userId: string): Promise<Offer[]> {
  return request<unknown>("/api/offers/pending", { userId }).then((payload) => asOffers(payload));
}

export function acceptOffer(userId: string, offerId: string, rideId: string): Promise<RideView> {
  return recordAction(getActivityClient(userId), "AcceptOffer", { rideId, offerId }, async (telemetry) => {
    const payload = await request<unknown>(`/api/offers/${encodeURIComponent(offerId)}/accept`, {
      method: "POST", userId, telemetry,
    });
    const ride = asRide(payload);
    if (ride) return ride;
    const current = await getCurrentRide(userId);
    if (!current) throw new Error("Accept did not return a ride");
    return current;
  });
}

export function declineOffer(userId: string, offerId: string, rideId: string): Promise<void> {
  return recordAction(getActivityClient(userId), "DeclineOffer", { rideId, offerId }, (telemetry) =>
    request<unknown>(`/api/offers/${encodeURIComponent(offerId)}/decline`, {
      method: "POST", userId, telemetry,
    }).then(() => undefined));
}

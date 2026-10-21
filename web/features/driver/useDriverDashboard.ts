"use client";

import { useEffect, useState } from "react";
import { acceptOffer, declineOffer, rideAction } from "@/features/rides/api";
import { useRideSession } from "@/features/rides/useRideSession";
import { earliestOffer } from "@/features/rides/offers";
import { getUsers } from "@/features/identity/api";
import type { DevUser } from "@/features/identity/types";
import type { Offer } from "@/features/rides/types";
import { centerForCity, DEFAULT_PICKUP } from "@/features/maps/geo";
import { messageFrom } from "@/shared/api/client";

export function useDriverDashboard(userId: string) {
  const { ride, offers, live, notice, session } = useRideSession(userId, "driver");
  const [me, setMe] = useState<DevUser | null>(null);
  const [center, setCenter] = useState({ lat: DEFAULT_PICKUP.lat, lng: DEFAULT_PICKUP.lng });
  const [error, setError] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);
  useEffect(() => {
    let cancel = false;
    getUsers(userId)
      .then((users) => {
        if (cancel) return;
        const person = users.find((user) => user.id === userId) ?? null;
        setMe(person);
        if (person) setCenter(centerForCity(person.city));
      })
      .catch(() => undefined);
    return () => {
      cancel = true;
    };
  }, [userId]);

  async function onAccept(offer: Offer) {
    setBusy(true);
    setError(null);
    try {
      await session.command(async () => {
        const accepted = await acceptOffer(userId, offer.offerId, offer.rideId);
        session.receiveRide(accepted);
        session.closeOffersForRide(accepted.rideId);
      });
    } catch (reason: unknown) {
      setError(messageFrom(reason));
    } finally {
      setBusy(false);
    }
  }

  async function onDecline(offer: Offer) {
    setBusy(true);
    setError(null);
    try {
      await session.command(async () => {
        await declineOffer(userId, offer.offerId, offer.rideId);
        session.closeOffer({ offerId: offer.offerId, reason: "Declined" });
      });
    } catch (reason: unknown) {
      setError(messageFrom(reason));
    } finally {
      setBusy(false);
    }
  }

  async function onAction(action: "arrived" | "start" | "dropoff") {
    if (!ride) return;
    const current = ride;
    const nextStatus = action === "arrived" ? "DriverArrived" : action === "start" ? "InProgress" : "DroppedOff";
    setBusy(true);
    setError(null);
    try {
      await session.command(async () => {
        const updated = await rideAction(userId, current.rideId, action);
        session.receiveRide(updated ?? { ...current, status: nextStatus });
      });
    } catch (reason: unknown) {
      setError(messageFrom(reason));
    } finally {
      setBusy(false);
    }
  }

  const offer = ride ? null : earliestOffer(offers);
  return { ride, live, offer, me, center, error, notice, busy, onAccept, onDecline, onAction };
}

export type DriverDashboard = ReturnType<typeof useDriverDashboard>;

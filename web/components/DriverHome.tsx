"use client";

import { useEffect, useRef, useState } from "react";
import {
  acceptOffer,
  declineOffer,
  getCurrentRide,
  getPendingOffers,
  getUsers,
  messageFrom,
  rideAction,
} from "@/lib/api";
import { expiryMs, formatKm } from "@/lib/format";
import { bearing, centerForCity, DEFAULT_PICKUP, isPlotted, linePoints } from "@/lib/geo";
import { applyDriverRide, isActiveStatus, preferForward, withEpoch } from "@/lib/rides";
import { openRideConnection } from "@/lib/signalr";
import type { DevUser, DriverLocation, Offer, RideView } from "@/lib/types";
import { MapCanvas, type MapMarker } from "@/components/MapCanvas";
import { ErrorLine, PrimaryButton, Stage, TextButton } from "@/components/ui";

export function DriverHome({ userId }: { userId: string }) {
  const [offers, setOffers] = useState<Offer[]>([]);
  const [ride, setRide] = useState<RideView | null>(null);
  const [live, setLive] = useState<DriverLocation | null>(null);
  const [me, setMe] = useState<DevUser | null>(null);
  const [center, setCenter] = useState({ lat: DEFAULT_PICKUP.lat, lng: DEFAULT_PICKUP.lng });
  const [error, setError] = useState<string | null>(null);
  const [notice, setNotice] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);
  const epoch = useRef(0);
  const offerSeen = useRef(new Map<string, number>());
  const offerClosed = useRef(new Map<string, number>());

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

  useEffect(() => {
    return openRideConnection(userId, {
      onRide: (incoming) => {
        if (incoming.status === "Cancelled") setNotice("Ride cancelled");
        if (!isActiveStatus(incoming.status)) setLive(null);
        setRide((previous) => applyDriverRide(previous, incoming));
      },
      onLocation: (location) => setLive(location),
      onOffer: (offer) => {
        offerSeen.current.set(offer.offerId, Date.now());
        setOffers((list) => upsertOffer(list, offer));
        setNotice(null);
      },
      onOfferClosed: (closed) => {
        offerClosed.current.set(closed.offerId, Date.now());
        setOffers((list) => list.filter((offer) => offer.offerId !== closed.offerId));
      },
    });
  }, [userId]);

  useEffect(() => {
    let stop = false;
    const tick = async () => {
      const started = Date.now();
      const stamp = epoch.current;
      try {
        const [pending, current] = await Promise.all([getPendingOffers(userId), getCurrentRide(userId)]);
        if (stop || stamp !== epoch.current) return;
        setOffers((existing) =>
          mergePolledOffers(existing, pending, started, offerSeen.current, offerClosed.current),
        );
        setRide((previous) => applyDriverRide(previous, current));
      } catch {
        // Polling is the backup when a hub event is missed.
      }
    };
    void tick();
    const id = window.setInterval(() => void tick(), 2000);
    return () => {
      stop = true;
      window.clearInterval(id);
    };
  }, [userId]);

  async function onAccept(offer: Offer) {
    setBusy(true);
    setError(null);
    try {
      const accepted = await withEpoch(epoch, () => acceptOffer(userId, offer.offerId));
      setRide((previous) => preferForward(previous, accepted));
      setOffers((list) => {
        for (const item of list) {
          if (item.rideId === accepted.rideId) offerClosed.current.set(item.offerId, Date.now());
        }
        return list.filter((item) => item.rideId !== accepted.rideId);
      });
      setNotice(null);
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
      offerClosed.current.set(offer.offerId, Date.now());
      await withEpoch(epoch, () => declineOffer(userId, offer.offerId));
      setOffers((list) => list.filter((item) => item.offerId !== offer.offerId));
    } catch (reason: unknown) {
      offerClosed.current.delete(offer.offerId);
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
      const updated = await withEpoch(epoch, () => rideAction(userId, current.rideId, action));
      setRide((previous) => preferForward(previous, updated ?? { ...current, status: nextStatus }));
    } catch (reason: unknown) {
      setError(messageFrom(reason));
    } finally {
      setBusy(false);
    }
  }

  const offer = ride ? null : earliestOffer(offers);
  const driverLive = live && ride && live.rideId === ride.rideId ? live : null;
  const driverPoint = driverLive
    ? { lat: driverLive.lat, lng: driverLive.lng }
    : ride?.driver
      ? { lat: ride.driver.lat, lng: ride.driver.lng }
      : null;
  const quoted = ride ? linePoints(ride.route) : [];
  const route =
    quoted.length >= 2
      ? quoted
      : ride && isPlotted(ride.pickup) && isPlotted(ride.dropoff)
        ? [ride.pickup, ride.dropoff]
        : offer && isPlotted(offer.pickup) && isPlotted(offer.dropoff)
          ? [offer.pickup, offer.dropoff]
          : [];
  const markers: MapMarker[] = [];
  const pickupPoint = ride?.pickup ?? offer?.pickup ?? null;
  const dropoffPoint = ride?.dropoff ?? offer?.dropoff ?? null;
  if (pickupPoint && isPlotted(pickupPoint) && (ride || offer)) markers.push({ ...pickupPoint, kind: "pickup" });
  if (dropoffPoint && isPlotted(dropoffPoint) && (ride || offer)) markers.push({ ...dropoffPoint, kind: "dropoff" });
  if (driverPoint && ride && isPlotted(driverPoint)) {
    const target = ride.status === "Accepted" || ride.status === "DriverArrived" ? ride.pickup : ride.dropoff;
    const heading = driverLive?.heading != null ? driverLive.heading : bearing(driverPoint, target);
    markers.push({ ...driverPoint, kind: "bike", heading });
  }

  const toDropoff = ride?.status === "InProgress" || ride?.status === "DroppedOff";
  const banner =
    ride && (ride.status === "Accepted" || ride.status === "DriverArrived" || toDropoff) ? (
      <div className="banner">
        <div className="kicker">{toDropoff ? "Drop-off" : "Pickup"}</div>
        <div className="strong" translate="no">
          {(toDropoff ? ride.dropoff.address : ride.pickup.address) || (toDropoff ? "Drop-off" : "Pickup")}
        </div>
      </div>
    ) : null;

  let sheet = (
    <div className="stack">
      <h1>You're online</h1>
      {me ? (
        <p className="strong" translate="no">
          {me.name}
        </p>
      ) : null}
      {me?.plate ? (
        <p className="muted" translate="no">
          {me.plate}
        </p>
      ) : null}
      <p className="muted">{notice ?? "Waiting for a ride request"}</p>
      <ErrorLine message={error} />
    </div>
  );

  if (offer) {
    sheet = (
      <OfferCard
        offer={offer}
        busy={busy}
        error={error}
        onAccept={() => void onAccept(offer)}
        onDecline={() => void onDecline(offer)}
      />
    );
  } else if (ride?.status === "DroppedOff") {
    sheet = (
      <div className="stack">
        <h1>Dropped off</h1>
        <p className="muted">Waiting for the rider to confirm</p>
        <ErrorLine message={error} />
      </div>
    );
  } else if (ride) {
    sheet = (
      <div className="stack">
        <ErrorLine message={error} />
        {ride.status === "Accepted" ? (
          <PrimaryButton data-testid="arrived" disabled={busy} onClick={() => void onAction("arrived")}>
            I've arrived
          </PrimaryButton>
        ) : null}
        {ride.status === "DriverArrived" ? (
          <PrimaryButton data-testid="pickup" disabled={busy} onClick={() => void onAction("start")}>
            Pick up
          </PrimaryButton>
        ) : null}
        {ride.status === "InProgress" ? (
          <PrimaryButton data-testid="dropoff" disabled={busy} onClick={() => void onAction("dropoff")}>
            Drop off
          </PrimaryButton>
        ) : null}
      </div>
    );
  }

  return (
    <Stage
      map={<MapCanvas center={pickupPoint && isPlotted(pickupPoint) ? pickupPoint : center} route={route} markers={markers} />}
      banner={banner}
      sheet={sheet}
    />
  );
}

function OfferCard({
  offer,
  busy,
  error,
  onAccept,
  onDecline,
}: {
  offer: Offer;
  busy: boolean;
  error: string | null;
  onAccept: () => void;
  onDecline: () => void;
}) {
  const [now, setNow] = useState(() => Date.now());
  const [windowMs, setWindowMs] = useState(() => offerWindow(offer.expiresAt));

  useEffect(() => {
    setWindowMs(offerWindow(offer.expiresAt));
    setNow(Date.now());
  }, [offer.offerId, offer.expiresAt]);

  useEffect(() => {
    const id = window.setInterval(() => setNow(Date.now()), 200);
    return () => window.clearInterval(id);
  }, []);

  const expiry = expiryMs(offer.expiresAt);
  const remaining = expiry === null ? windowMs : Math.max(0, expiry - now);
  const fraction = Math.max(0, Math.min(1, remaining / windowMs));
  const seconds = Math.ceil(remaining / 1000);

  return (
    <div className="stack" data-testid="offer-card">
      <div className="title-row">
        <h1>Motorbike</h1>
        {expiry !== null ? <div className="eta">{seconds} sec</div> : null}
      </div>
      <div className="track" aria-hidden="true">
        <div className="bar" style={{ width: `${fraction * 100}%` }} />
      </div>
      <p className="muted">{formatKm(offer.pickupDistanceM)} to pickup</p>
      <div className="stops">
        <div className="stop">
          <span className="stop-mark dot" />
          <div>
            <div translate="no">{offer.pickup.address || "Pickup"}</div>
            <div className="muted">Pickup</div>
          </div>
        </div>
        <div className="stop">
          <span className="stop-mark square" />
          <div>
            <div translate="no">{offer.dropoff.address || "Drop-off"}</div>
            <div className="muted">Drop-off</div>
          </div>
        </div>
      </div>
      <ErrorLine message={error} />
      <PrimaryButton data-testid="accept-ride" disabled={busy} onClick={onAccept}>
        Accept ride
      </PrimaryButton>
      <TextButton data-testid="decline-ride" disabled={busy} onClick={onDecline}>
        Decline
      </TextButton>
    </div>
  );
}

function offerWindow(expiresAt: string): number {
  const expiry = expiryMs(expiresAt);
  if (expiry === null) return 30_000;
  return Math.max(1000, expiry - Date.now());
}

function earliestOffer(offers: Offer[]): Offer | null {
  if (offers.length === 0) return null;
  return [...offers].sort(
    (left, right) =>
      (expiryMs(left.expiresAt) ?? Number.MAX_SAFE_INTEGER) - (expiryMs(right.expiresAt) ?? Number.MAX_SAFE_INTEGER),
  )[0] ?? null;
}

function upsertOffer(list: Offer[], offer: Offer): Offer[] {
  const index = list.findIndex((item) => item.offerId === offer.offerId);
  if (index === -1) return [offer, ...list];
  const next = list.slice();
  next[index] = offer;
  return next;
}

function mergePolledOffers(
  existing: Offer[],
  incoming: Offer[],
  started: number,
  seen: Map<string, number>,
  closed: Map<string, number>,
): Offer[] {
  const fresh = incoming.filter((offer) => !closed.has(offer.offerId));
  const raced = existing.filter((offer) => {
    if (closed.has(offer.offerId)) return false;
    const at = seen.get(offer.offerId) ?? 0;
    return at > started && !fresh.some((item) => item.offerId === offer.offerId);
  });
  return [...fresh, ...raced];
}

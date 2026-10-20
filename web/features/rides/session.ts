import { upsertOffer } from "./offers";
import { applyRiderUpdate, isActiveStatus, preferForward } from "./reconcile";
import type { DriverLocation, Offer, OfferClosed, RideView } from "./types";

export type SessionRole = "rider" | "driver";
export type SessionSnapshot = {
  ride: RideView | null;
  offers: Offer[];
  live: DriverLocation | null;
  notice: string | null;
};
export type PolledSnapshot = { ride: RideView | null; offers: Offer[] };

// A session owns ordering across HTTP commands, polling and hub events.
// Tombstones last for this mounted persona, so delayed snapshots cannot revive
// dismissed rides or closed offers. Location events do not invalidate polling.
export function createRideSession(role: SessionRole) {
  let state: SessionSnapshot = { ride: null, offers: [], live: null, notice: null };
  let revision = 0;
  let commands = 0;
  const ignoredRides = new Set<string>();
  const closedRides = new Set<string>();
  const closedOffers = new Set<string>();
  const listeners = new Set<() => void>();

  function publish(next: SessionSnapshot) {
    state = next;
    for (const listener of listeners) listener();
  }

  function reconcileRide(incoming: RideView | null): SessionSnapshot {
    if (incoming && ignoredRides.has(incoming.rideId)) return state;
    if (role === "rider") {
      return { ...state, ride: applyRiderUpdate(state.ride, incoming, ignoredRides) };
    }
    if (incoming && !isActiveStatus(incoming.status)) {
      closedRides.add(incoming.rideId);
      if (state.ride && state.ride.rideId !== incoming.rideId) return state;
      return {
        ...state,
        ride: null,
        live: null,
        notice: incoming.status === "Cancelled" ? "Ride cancelled" : state.notice,
      };
    }
    if (incoming && closedRides.has(incoming.rideId)) return state;
    return { ...state, ride: incoming ? preferForward(state.ride, incoming) : null };
  }

  const session = {
    getSnapshot: () => state,
    subscribe(listener: () => void) {
      listeners.add(listener);
      return () => { listeners.delete(listener); };
    },
    pollRevision: () => commands === 0 ? revision : null,
    invalidate() { revision += 1; },
    receiveRide(incoming: RideView) {
      revision += 1;
      publish(reconcileRide(incoming));
    },
    receiveLocation(live: DriverLocation) {
      publish({ ...state, live });
    },
    receiveOffer(offer: Offer) {
      revision += 1;
      if (closedOffers.has(offer.offerId) || closedRides.has(offer.rideId)) return;
      publish({ ...state, offers: upsertOffer(state.offers, offer), notice: null });
    },
    closeOffer(closed: OfferClosed) {
      revision += 1;
      closedOffers.add(closed.offerId);
      publish({ ...state, offers: state.offers.filter((offer) => offer.offerId !== closed.offerId) });
    },
    closeOffersForRide(rideId: string) {
      revision += 1;
      for (const offer of state.offers) {
        if (offer.rideId === rideId) closedOffers.add(offer.offerId);
      }
      publish({ ...state, offers: state.offers.filter((offer) => offer.rideId !== rideId), notice: null });
    },
    receivePoll(stamp: number, incoming: PolledSnapshot) {
      if (stamp !== revision || commands > 0) return;
      const next = reconcileRide(incoming.ride);
      publish({
        ...next,
        offers: incoming.offers.filter((offer) =>
          !closedOffers.has(offer.offerId) && !closedRides.has(offer.rideId)),
      });
    },
    dismissRide() {
      revision += 1;
      if (state.ride) ignoredRides.add(state.ride.rideId);
      publish({ ...state, ride: null, live: null });
    },
    async command<T>(task: () => Promise<T>): Promise<T> {
      commands += 1;
      revision += 1;
      try {
        return await task();
      } finally {
        commands -= 1;
        revision += 1;
      }
    },
  };
  return session;
}

export type RideSession = ReturnType<typeof createRideSession>;

export type RideHandlers = {
  onRide: (ride: RideView) => void;
  onLocation: (location: DriverLocation) => void;
  onOffer: (offer: Offer) => void;
  onOfferClosed: (closed: OfferClosed) => void;
};

type SyncDependencies = {
  load: () => Promise<PolledSnapshot>;
  connect: (handlers: RideHandlers) => () => void;
  schedule: (callback: () => void, delay: number) => () => void;
};

// Schedule after each completed request to avoid overlapping polls. Both
// personas share cancellation and stale-response rules through this owner.
export function synchronizeRideSession(session: RideSession, dependencies: SyncDependencies): () => void {
  let active = true;
  let cancelTimer = () => {};
  const closeConnection = dependencies.connect({
    onRide: (ride) => { if (active) session.receiveRide(ride); },
    onLocation: (location) => { if (active) session.receiveLocation(location); },
    onOffer: (offer) => { if (active) session.receiveOffer(offer); },
    onOfferClosed: (closed) => { if (active) session.closeOffer(closed); },
  });
  const tick = async () => {
    if (!active) return;
    const stamp = session.pollRevision();
    try {
      if (stamp !== null) {
        const incoming = await dependencies.load();
        if (active) session.receivePoll(stamp, incoming);
      }
    } catch {
      // Poll again after transient failures; HTTP remains the hub fallback.
    } finally {
      if (active) cancelTimer = dependencies.schedule(() => void tick(), 2000);
    }
  };
  void tick();
  return () => {
    active = false;
    session.invalidate();
    cancelTimer();
    closeConnection();
  };
}

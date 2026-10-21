"use client";

import { useEffect, useMemo, useSyncExternalStore } from "react";
import { getCurrentRide, getPendingOffers } from "./api";
import { createRideSession, synchronizeRideSession, type SessionRole } from "./session";
import { openRideConnection } from "./signalr";
import { mountActivity } from "@/shared/telemetry/browser";
import { observeRideActivity } from "./activity";

export function useRideSession(userId: string, role: SessionRole) {
  const session = useMemo(() => createRideSession(role), [userId, role]);
  const state = useSyncExternalStore(session.subscribe, session.getSnapshot, session.getSnapshot);

  useEffect(() => {
    const activity = mountActivity(userId);
    const stopObserving = activity.client ? observeRideActivity(session, role, activity.client) : () => {};
    const stopSynchronizing = synchronizeRideSession(session, {
      load: async () => {
        const [ride, offers] = await Promise.all([
          getCurrentRide(userId),
          role === "driver" ? getPendingOffers(userId) : Promise.resolve([]),
        ]);
        return { ride, offers };
      },
      connect: (handlers) => openRideConnection(userId, handlers, activity.client),
      schedule: (callback, delay) => {
        const timer = window.setTimeout(callback, delay);
        return () => window.clearTimeout(timer);
      },
    });
    return () => {
      stopObserving();
      stopSynchronizing();
      activity.dispose();
    };
  }, [session, userId, role]);

  return { ...state, session };
}

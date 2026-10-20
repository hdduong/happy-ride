import { asLocation, asOffer, asOfferClosed, asRide } from "@/lib/parse";
import type { DriverLocation, Offer, OfferClosed, RideView } from "@/lib/types";
import { HubConnection, HubConnectionBuilder, HubConnectionState, LogLevel } from "@microsoft/signalr";
import { apiBase } from "@/lib/api";

export type RideHandlers = {
  onRide?: (ride: RideView) => void;
  onLocation?: (location: DriverLocation) => void;
  onOffer?: (offer: Offer) => void;
  onOfferClosed?: (closed: OfferClosed) => void;
};

export function createRideConnection(userId: string, handlers: RideHandlers): HubConnection {
  const connection = new HubConnectionBuilder()
    .withUrl(`${apiBase()}/hubs/ride?userId=${encodeURIComponent(userId)}`, {
      headers: { "X-User-Id": userId },
    })
    .withAutomaticReconnect([0, 2000, 5000, 10000])
    .configureLogging(LogLevel.Warning)
    .build();

  connection.on("RideStatusChanged", (payload: unknown) => {
    const ride = asRide(payload);
    if (ride) handlers.onRide?.(ride);
  });
  connection.on("DriverLocation", (payload: unknown) => {
    const location = asLocation(payload);
    if (location) handlers.onLocation?.(location);
  });
  connection.on("OfferReceived", (payload: unknown) => {
    const offer = asOffer(payload);
    if (offer) handlers.onOffer?.(offer);
  });
  connection.on("OfferClosed", (payload: unknown) => {
    const closed = asOfferClosed(payload);
    if (closed) handlers.onOfferClosed?.(closed);
  });

  return connection;
}

export function openRideConnection(userId: string, handlers: RideHandlers): () => void {
  const connection = createRideConnection(userId, handlers);
  let closed = false;
  void connection
    .start()
    .then(() => {
      if (closed) return connection.stop().catch(() => undefined);
    })
    .catch((error: unknown) => {
      if (!closed) console.warn("SignalR connection failed", error);
    });

  return () => {
    closed = true;
    if (connection.state === HubConnectionState.Connected) {
      void connection.stop().catch(() => undefined);
    }
  };
}

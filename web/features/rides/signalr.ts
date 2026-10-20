import { HubConnectionBuilder, LogLevel } from "@microsoft/signalr";
import { apiBase } from "@/shared/api/client";
import { asLocation, asOffer, asOfferClosed, asRide } from "./parse";
import { openConnection } from "./connectionLifecycle";
import type { RideHandlers } from "./session";

export function openRideConnection(userId: string, handlers: RideHandlers): () => void {
  return openConnection((isActive) => {
    const connection = new HubConnectionBuilder()
      .withUrl(`${apiBase()}/hubs/ride?userId=${encodeURIComponent(userId)}`, {
        headers: { "X-User-Id": userId },
      })
      .withAutomaticReconnect([0, 2000, 5000, 10000])
      .configureLogging(LogLevel.Warning)
      .build();

    function register<T>(event: string, parse: (payload: unknown) => T | null, handle: (value: T) => void) {
      connection.on(event, (payload: unknown) => {
        if (!isActive()) return;
        const value = parse(payload);
        if (value !== null) handle(value);
      });
    }
    register("RideStatusChanged", asRide, handlers.onRide);
    register("DriverLocation", asLocation, handlers.onLocation);
    register("OfferReceived", asOffer, handlers.onOffer);
    register("OfferClosed", asOfferClosed, handlers.onOfferClosed);
    return { connection };
  }, (error) => console.warn("SignalR connection failed", error));
}

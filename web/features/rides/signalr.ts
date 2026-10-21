import { HubConnectionBuilder, LogLevel } from "@microsoft/signalr";
import { realtimeBase } from "@/shared/api/base";
import { asLocation, asOffer, asOfferClosed, asRide } from "./parse";
import { openConnection } from "./connectionLifecycle";
import { observeConnectionActivity } from "./connectionActivity";
import type { RideHandlers } from "./session";
import type { ActivityClient } from "@/shared/telemetry/activity";

async function readRealtimeToken(): Promise<string> {
  if (typeof window === "undefined") return "";
  const response = await fetch("/backend/realtime-token");
  if (!response.ok) return "";
  const body = (await response.json()) as { token?: string | null };
  return body.token ?? "";
}

export function openRideConnection(userId: string, handlers: RideHandlers, activity?: ActivityClient): () => void {
  let lifecycle: ReturnType<typeof observeConnectionActivity>;
  return openConnection((isActive) => {
    const sessionQuery = activity ? `&appSessionId=${encodeURIComponent(activity.appSessionId)}` : "";
    const connection = new HubConnectionBuilder()
      .withUrl(`${realtimeBase()}/hubs/ride?userId=${encodeURIComponent(userId)}${sessionQuery}`, {
        headers: { "X-User-Id": userId },
        accessTokenFactory: readRealtimeToken,
      })
      .withAutomaticReconnect()
      .configureLogging({
        log(level: LogLevel, message: string) {
          if (level < LogLevel.Warning) return;
          // Next.js treats console.error as a page issue. A dropped socket while the API restarts is expected.
          console.debug(message);
        },
      })
      .build();

    lifecycle = observeConnectionActivity(connection, isActive, activity);

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
    connection.on("MapConfigChanged", () => {
      if (!isActive()) return;
      window.dispatchEvent(new Event("happy-ride-map-config"));
    });
    return { connection };
  }, () => lifecycle.failed(), () => lifecycle.connected());
}

import { DefaultHttpClient, HttpTransportType, HubConnectionBuilder, LogLevel, NullLogger, type HttpRequest, type HttpResponse } from "@microsoft/signalr";
import { apiBase } from "@/shared/api/client";
import { currentPersonaProof, ensurePersonaSession } from "@/shared/api/personaSession";
import { bindMapConfigRefresh } from "@/features/maps/refresh";
import { asLocation, asOffer, asOfferClosed, asRide } from "./parse";
import { openConnection } from "./connectionLifecycle";
import { observeConnectionActivity } from "./connectionActivity";
import type { RideHandlers } from "./session";
import type { ActivityClient } from "@/shared/telemetry/activity";

class PersonaHttpClient extends DefaultHttpClient {
  public constructor(private readonly userId: string) {
    super(NullLogger.instance);
  }

  public send(request: HttpRequest): Promise<HttpResponse> {
    const proof = currentPersonaProof(this.userId);
    if (proof) {
      request.headers ??= {};
      request.headers["X-Persona"] = proof;
    }
    return super.send(request);
  }
}

export function openRideConnection(userId: string, handlers: RideHandlers, activity?: ActivityClient): () => void {
  let lifecycle: ReturnType<typeof observeConnectionActivity>;
  return openConnection((isActive) => {
    const sessionQuery = activity ? `&appSessionId=${encodeURIComponent(activity.appSessionId)}` : "";
    const connection = new HubConnectionBuilder()
      .withUrl(`${apiBase()}/hubs/ride?userId=${encodeURIComponent(userId)}${sessionQuery}`, {
        // Long polling stays on this server, which adds the Entra token.
        // A browser WebSocket cannot carry that server-only credential.
        transport: HttpTransportType.LongPolling,
        headers: { "X-User-Id": userId },
        httpClient: new PersonaHttpClient(userId),
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
    const start = connection.start.bind(connection);
    connection.start = () => ensurePersonaSession(userId).then(start);

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
    bindMapConfigRefresh(connection, isActive);
    return { connection };
  }, () => lifecycle.failed(), () => lifecycle.connected());
}

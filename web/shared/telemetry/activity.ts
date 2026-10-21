export type ActivityName =
  | "AppOpened" | "AppBackgrounded" | "AppResumed" | "AppClosed"
  | "RideContextStarted" | "RideContextEnded" | "OfferDisplayed"
  | "ActionRequested" | "ActionSucceeded" | "ActionFailed"
  | "ConnectionConnected" | "ConnectionReconnecting" | "ConnectionReconnected"
  | "ConnectionClosed" | "ConnectionFailed";
export type ActivityAction = "Book" | "AcceptOffer" | "DeclineOffer" | "Cancel"
  | "ConfirmArrival" | "Arrived" | "Start" | "Dropoff";
export type Correlation = { appSessionId: string; activityId?: string; rideId?: string };
export type ActivityDetails = {
  activityId?: string;
  action?: ActivityAction;
  rideId?: string;
  offerId?: string;
  connectionId?: string;
};
export type Activity = ActivityDetails & {
  activityId: string;
  appSessionId: string;
  eventName: ActivityName;
  clientOccurredAt: string;
};
type Dependencies = {
  id: () => string;
  now: () => Date;
  send: (activity: Activity) => Promise<"sent" | "retry" | "drop">;
  schedule: (callback: () => void, delay: number) => () => void;
};

// Small, memory-only, best-effort queue. Telemetry never blocks a ride command.
// Each entry captures its context when created, including across reconnects.
export function createActivityClient(appSessionId: string, dependencies: Dependencies) {
  const queue: { activity: Activity; attempts: number }[] = [];
  let rideId: string | undefined;
  let sending = false;
  let waiting = false;
  let cancelRetry = () => {};
  let closed = false;

  async function drain() {
    if (sending || waiting || !queue.length) return;
    sending = true;
    const entry = queue[0];
    let outcome: "sent" | "retry" | "drop" = "retry";
    try { outcome = await dependencies.send(entry.activity); } catch { /* best effort */ }
    sending = false;
    entry.attempts += 1;
    if (outcome !== "retry" || entry.attempts >= 3 || closed) {
      if (queue[0] === entry) queue.shift();
      void drain();
    } else {
      waiting = true;
      cancelRetry = dependencies.schedule(() => {
        waiting = false;
        void drain();
      }, 1000 * entry.attempts);
    }
  }

  function track(eventName: ActivityName, details: ActivityDetails = {}) {
    if (closed || queue.length >= 64) return;
    try {
      queue.push({
        activity: {
          appSessionId, rideId, ...details,
          activityId: details.activityId ?? dependencies.id(),
          eventName, clientOccurredAt: dependencies.now().toISOString(),
        },
        attempts: 0,
      });
      void drain();
    } catch { /* Telemetry must not change application behavior. */ }
  }

  return {
    appSessionId,
    track,
    correlation: (): Correlation => ({ appSessionId, rideId }),
    newActivityId: () => dependencies.id(),
    setRideContext(nextRideId: string | undefined) {
      if (rideId === nextRideId) return;
      if (rideId) track("RideContextEnded");
      rideId = nextRideId;
      if (rideId) track("RideContextStarted");
    },
    flush() {
      cancelRetry();
      waiting = false;
      void drain();
    },
    close() {
      // Finish bounded pending delivery, without keeping retry timers alive.
      closed = true;
      cancelRetry();
      waiting = false;
      void drain();
    },
  };
}

export type ActivityClient = ReturnType<typeof createActivityClient>;

export async function recordAction<T>(
  client: ActivityClient | undefined,
  action: ActivityAction,
  details: Pick<ActivityDetails, "rideId" | "offerId">,
  task: (correlation: Correlation | undefined) => Promise<T>,
  resultRideId?: (result: T) => string | undefined,
): Promise<T> {
  let activityId: string | undefined;
  try { activityId = client?.newActivityId(); } catch { /* best effort */ }
  const attempt = { ...details, action, activityId };
  client?.track("ActionRequested", attempt);
  const correlation = client ? { appSessionId: client.appSessionId, activityId, rideId: details.rideId } : undefined;
  try {
    const result = await task(correlation);
    client?.track("ActionSucceeded", { ...attempt, rideId: resultRideId?.(result) ?? details.rideId });
    return result;
  } catch (error) {
    client?.track("ActionFailed", attempt);
    throw error;
  }
}

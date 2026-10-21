import { apiBase } from "../api/base";
import { createActivityClient, type Activity, type ActivityClient } from "./activity";

type MountedSession = { client: ActivityClient; owners: number; detach: () => void };
const sessions = new Map<string, MountedSession>();
const storageKey = "happy-ride-activity-session";

function sessionId(userId: string): string {
  // sessionStorage is per tab. Changing persona rotates the session, even if
  // that tab subsequently switches back to a previously selected persona.
  let saved: { userId?: string; appSessionId?: string } | null = null;
  try { saved = JSON.parse(sessionStorage.getItem(storageKey) ?? "null"); } catch { /* storage can be denied */ }
  if (saved?.userId === userId && /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i.test(saved.appSessionId ?? "")) {
    return saved.appSessionId!;
  }
  const appSessionId = crypto.randomUUID();
  try { sessionStorage.setItem(storageKey, JSON.stringify({ userId, appSessionId })); } catch { /* memory fallback */ }
  return appSessionId;
}

async function send(userId: string, activity: Activity): Promise<"sent" | "retry" | "drop"> {
  const controller = new AbortController();
  const timer = setTimeout(() => controller.abort(), 3000);
  try {
    const headers: Record<string, string> = {
      "Content-Type": "application/json", "X-User-Id": userId,
      "X-App-Session-Id": activity.appSessionId, "X-Activity-Id": activity.activityId,
    };
    if (activity.rideId) headers["X-Ride-Id"] = activity.rideId;
    const response = await fetch(`${apiBase()}/api/telemetry/activity`, {
      method: "POST", keepalive: true, signal: controller.signal,
      headers,
      body: JSON.stringify(activity),
    });
    return response.ok ? "sent" : response.status === 429 || response.status >= 500 ? "retry" : "drop";
  } catch {
    return "retry";
  } finally {
    clearTimeout(timer);
  }
}

export function getActivityClient(userId: string | null | undefined): ActivityClient | undefined {
  const session = userId ? sessions.get(userId) : undefined;
  return session && session.owners > 0 ? session.client : undefined;
}

// The deferred final release absorbs React StrictMode's setup/cleanup/setup.
// An old persona's asynchronous actions retain their own client and identity.
export function mountActivity(userId: string): { client?: ActivityClient; dispose: () => void } {
  try {
    let mounted = sessions.get(userId);
    if (!mounted) {
      const client = createActivityClient(sessionId(userId), {
        id: () => crypto.randomUUID(), now: () => new Date(),
        send: (activity) => send(userId, activity),
        schedule: (callback, delay) => {
          const timer = setTimeout(callback, delay);
          return () => clearTimeout(timer);
        },
      });
      let hidden = document.visibilityState === "hidden";
      let pageClosed = false;
      const visibility = () => {
        const nextHidden = document.visibilityState === "hidden";
        if (hidden === nextHidden) return;
        hidden = nextHidden;
        client.track(hidden ? "AppBackgrounded" : "AppResumed");
        client.flush();
      };
      const pagehide = () => {
        if (pageClosed) return;
        pageClosed = true;
        client.track("AppClosed");
        client.flush();
      };
      const pageshow = (event: PageTransitionEvent) => {
        if (!event.persisted || !pageClosed) return;
        pageClosed = false;
        hidden = document.visibilityState === "hidden";
        client.track("AppResumed");
        client.flush();
      };
      const online = () => client.flush();
      document.addEventListener("visibilitychange", visibility);
      window.addEventListener("pagehide", pagehide);
      window.addEventListener("pageshow", pageshow);
      window.addEventListener("online", online);
      mounted = { client, owners: 0, detach: () => {
        document.removeEventListener("visibilitychange", visibility);
        window.removeEventListener("pagehide", pagehide);
        window.removeEventListener("pageshow", pageshow);
        window.removeEventListener("online", online);
        if (!pageClosed) client.track("AppClosed");
        client.close();
      } };
      sessions.set(userId, mounted);
      client.track("AppOpened");
    }
    const entry = mounted;
    entry.owners += 1;
    let disposed = false;
    return { client: entry.client, dispose: () => {
      if (disposed) return;
      disposed = true;
      entry.owners -= 1;
      queueMicrotask(() => {
        if (entry.owners !== 0) return;
        entry.detach();
        if (sessions.get(userId) === entry) sessions.delete(userId);
      });
    } };
  } catch {
    return { dispose: () => {} };
  }
}

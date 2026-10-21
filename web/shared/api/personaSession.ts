let sessionUser: string | null = null;
let sessionUntil = 0;
const renewAfterMs = 7 * 60 * 60 * 1000;

export function forgetPersonaSession(userId?: string): void {
  if (!userId || sessionUser === userId) {
    sessionUser = null;
    sessionUntil = 0;
  }
}

export async function ensurePersonaSession(userId: string): Promise<void> {
  if (typeof window === "undefined") return;
  if (sessionUser === userId && Date.now() < sessionUntil) return;
  const response = await fetch("/backend/session", {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify({ userId }),
  });
  if (!response.ok) {
    const text = await response.text();
    let payload: unknown = null;
    if (text) {
      try {
        payload = JSON.parse(text) as unknown;
      } catch {
        payload = { error: text };
      }
    }
    const detail = payload && typeof payload === "object" && "error" in payload && typeof payload.error === "string"
      ? payload.error
      : `Request failed (${response.status})`;
    throw new Error(detail);
  }
  sessionUser = userId;
  sessionUntil = Date.now() + renewAfterMs;
}

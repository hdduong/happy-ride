const renewWithinMs = 60 * 60 * 1000;
const disabledKey = "happy-ride-persona-off";

function proofKey(userId: string): string {
  return `happy-ride-persona-proof:${userId.toLowerCase()}`;
}

function personaOff(): boolean {
  try {
    return sessionStorage.getItem(disabledKey) === "1";
  } catch {
    return false;
  }
}

export function currentPersonaProof(userId: string): string | null {
  if (typeof window === "undefined") return null;
  try {
    return sessionStorage.getItem(proofKey(userId));
  } catch {
    return null;
  }
}

export function forgetPersonaSession(userId: string): void {
  if (typeof window === "undefined") return;
  try {
    sessionStorage.removeItem(proofKey(userId));
    sessionStorage.removeItem(disabledKey);
  } catch {
    /* storage can be denied */
  }
}

function proofStillFresh(proof: string, userId: string): boolean {
  const [storedId, expiresAt] = proof.split(".");
  const expiry = Number(expiresAt);
  return storedId.toLowerCase() === userId.toLowerCase() && Number.isFinite(expiry) && expiry * 1000 - Date.now() > renewWithinMs;
}

export async function ensurePersonaSession(userId: string): Promise<void> {
  if (typeof window === "undefined" || personaOff()) return;
  const existing = currentPersonaProof(userId);
  if (existing && proofStillFresh(existing, userId)) return;
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
  if (response.status === 204) {
    try {
      sessionStorage.setItem(disabledKey, "1");
    } catch {
      /* the next call can ask again */
    }
    return;
  }
  const body = (await response.json()) as { proof?: unknown };
  if (typeof body.proof !== "string") throw new Error("The persona session was incomplete");
  try {
    sessionStorage.removeItem(disabledKey);
    sessionStorage.setItem(proofKey(userId), body.proof);
  } catch {
    throw new Error("The persona session could not be stored");
  }
}

const proofKey = "happy-ride-persona-proof";
const renewWithinMs = 60 * 60 * 1000;

export function currentPersonaProof(): string | null {
  if (typeof window === "undefined") return null;
  try {
    return sessionStorage.getItem(proofKey);
  } catch {
    return null;
  }
}

export function forgetPersonaSession(userId?: string): void {
  if (typeof window === "undefined") return;
  const proof = currentPersonaProof();
  if (!userId || !proof || proof.startsWith(`${userId}.`)) {
    try {
      sessionStorage.removeItem(proofKey);
    } catch {
      /* storage can be denied */
    }
  }
}

function proofStillFresh(proof: string, userId: string): boolean {
  const expiresAt = Number(proof.split(".")[1]);
  return proof.startsWith(`${userId}.`) && Number.isFinite(expiresAt) && expiresAt * 1000 - Date.now() > renewWithinMs;
}

export async function ensurePersonaSession(userId: string): Promise<void> {
  if (typeof window === "undefined") return;
  const existing = currentPersonaProof();
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
  if (response.status === 204) return;
  const body = (await response.json()) as { proof?: unknown };
  if (typeof body.proof !== "string") throw new Error("The persona session was incomplete");
  try {
    sessionStorage.setItem(proofKey, body.proof);
  } catch {
    throw new Error("The persona session could not be stored");
  }
}

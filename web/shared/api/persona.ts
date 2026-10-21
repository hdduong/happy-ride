import { createHmac, timingSafeEqual } from "node:crypto";

export function sealPersona(userId: string, secret: string, expiresAtSeconds: number): string {
  const payload = `${userId}.${expiresAtSeconds}`;
  const signature = createHmac("sha256", secret).update(payload).digest("base64url");
  return `${payload}.${signature}`;
}

export function openPersona(value: string | undefined, secret: string, nowMs = Date.now()): string | null {
  if (!value) return null;
  const parts = value.split(".");
  if (parts.length !== 3) return null;
  const [userId, expiresAt, signature] = parts;
  const expiry = Number(expiresAt);
  if (!userId || !Number.isFinite(expiry) || expiry * 1000 <= nowMs) return null;
  const expected = createHmac("sha256", secret).update(`${userId}.${expiresAt}`).digest("base64url");
  const left = Buffer.from(signature);
  const right = Buffer.from(expected);
  if (left.length !== right.length || !timingSafeEqual(left, right)) return null;
  return userId;
}

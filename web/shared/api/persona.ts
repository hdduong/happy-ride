import { createHmac, timingSafeEqual } from "node:crypto";

export const personaCookieName = "hr-persona";

export function sealPersona(userId: string, secret: string): string {
  const signature = createHmac("sha256", secret).update(userId).digest("base64url");
  return `${userId}.${signature}`;
}

export function openPersona(value: string | undefined, secret: string): string | null {
  if (!value) return null;
  const split = value.lastIndexOf(".");
  if (split <= 0) return null;
  const userId = value.slice(0, split);
  const signature = value.slice(split + 1);
  const expected = createHmac("sha256", secret).update(userId).digest("base64url");
  const left = Buffer.from(signature);
  const right = Buffer.from(expected);
  if (left.length !== right.length || !timingSafeEqual(left, right)) return null;
  return userId;
}

export function personaFromCookieHeader(header: string | null, secret: string): string | null {
  if (!header) return null;
  const pair = header.split(";").map((part) => part.trim()).find((part) => part.startsWith(`${personaCookieName}=`));
  if (!pair) return null;
  return openPersona(decodeURIComponent(pair.slice(personaCookieName.length + 1)), secret);
}

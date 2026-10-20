import { expiryMs } from "../../shared/format";
import type { Offer } from "./types";

export function earliestOffer(offers: Offer[]): Offer | null {
  if (offers.length === 0) return null;
  return [...offers].sort(
    (left, right) =>
      (expiryMs(left.expiresAt) ?? Number.MAX_SAFE_INTEGER) - (expiryMs(right.expiresAt) ?? Number.MAX_SAFE_INTEGER),
  )[0] ?? null;
}

export function upsertOffer(list: Offer[], offer: Offer): Offer[] {
  const index = list.findIndex((item) => item.offerId === offer.offerId);
  if (index === -1) return [offer, ...list];
  const next = list.slice();
  next[index] = offer;
  return next;
}

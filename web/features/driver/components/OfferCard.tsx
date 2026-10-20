"use client";

import { useEffect, useState } from "react";
import { expiryMs, formatKm } from "@/shared/format";
import type { Offer } from "@/features/rides/types";
import { ErrorLine, PrimaryButton, TextButton } from "@/shared/ui";

export function OfferCard({
  offer,
  busy,
  error,
  onAccept,
  onDecline,
}: {
  offer: Offer;
  busy: boolean;
  error: string | null;
  onAccept: () => void;
  onDecline: () => void;
}) {
  const [now, setNow] = useState(() => Date.now());
  const [windowMs, setWindowMs] = useState(() => offerWindow(offer.expiresAt));

  useEffect(() => {
    setWindowMs(offerWindow(offer.expiresAt));
    setNow(Date.now());
  }, [offer.offerId, offer.expiresAt]);

  useEffect(() => {
    const id = window.setInterval(() => setNow(Date.now()), 200);
    return () => window.clearInterval(id);
  }, []);

  const expiry = expiryMs(offer.expiresAt);
  const remaining = expiry === null ? windowMs : Math.max(0, expiry - now);
  const fraction = Math.max(0, Math.min(1, remaining / windowMs));
  const seconds = Math.ceil(remaining / 1000);

  return (
    <div className="stack" data-testid="offer-card">
      <div className="title-row">
        <h1>Motorbike</h1>
        {expiry !== null ? <div className="eta">{seconds} sec</div> : null}
      </div>
      <div className="track" aria-hidden="true">
        <div className="bar" style={{ width: `${fraction * 100}%` }} />
      </div>
      <p className="muted">{formatKm(offer.pickupDistanceM)} to pickup</p>
      <div className="stops">
        <div className="stop">
          <span className="stop-mark dot" />
          <div>
            <div translate="no">{offer.pickup.address || "Pickup"}</div>
            <div className="muted">Pickup</div>
          </div>
        </div>
        <div className="stop">
          <span className="stop-mark square" />
          <div>
            <div translate="no">{offer.dropoff.address || "Drop-off"}</div>
            <div className="muted">Drop-off</div>
          </div>
        </div>
      </div>
      <ErrorLine message={error} />
      <PrimaryButton data-testid="accept-ride" disabled={busy} onClick={onAccept}>
        Accept ride
      </PrimaryButton>
      <TextButton data-testid="decline-ride" disabled={busy} onClick={onDecline}>
        Decline
      </TextButton>
    </div>
  );
}

function offerWindow(expiresAt: string): number {
  const expiry = expiryMs(expiresAt);
  if (expiry === null) return 30_000;
  return Math.max(1000, expiry - Date.now());
}

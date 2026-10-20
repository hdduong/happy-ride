import { ErrorLine, PrimaryButton } from "@/shared/ui";
import { OfferCard } from "./OfferCard";
import type { DriverDashboard } from "../useDriverDashboard";

export function DriverSheet({ dashboard }: { dashboard: DriverDashboard }) {
  const { ride, offer, me, error, notice, busy, onAccept, onDecline, onAction } = dashboard;
  let sheet = (
    <div className="stack">
      <h1>You're online</h1>
      {me ? (
        <p className="strong" translate="no">
          {me.name}
        </p>
      ) : null}
      {me?.plate ? (
        <p className="muted" translate="no">
          {me.plate}
        </p>
      ) : null}
      <p className="muted">{notice ?? "Waiting for a ride request"}</p>
      <ErrorLine message={error} />
    </div>
  );

  if (offer) {
    sheet = (
      <OfferCard
        offer={offer}
        busy={busy}
        error={error}
        onAccept={() => void onAccept(offer)}
        onDecline={() => void onDecline(offer)}
      />
    );
  } else if (ride?.status === "DroppedOff") {
    sheet = (
      <div className="stack">
        <h1>Dropped off</h1>
        <p className="muted">Waiting for the rider to confirm</p>
        <ErrorLine message={error} />
      </div>
    );
  } else if (ride) {
    sheet = (
      <div className="stack">
        <ErrorLine message={error} />
        {ride.status === "Accepted" ? (
          <PrimaryButton data-testid="arrived" disabled={busy} onClick={() => void onAction("arrived")}>
            I've arrived
          </PrimaryButton>
        ) : null}
        {ride.status === "DriverArrived" ? (
          <PrimaryButton data-testid="pickup" disabled={busy} onClick={() => void onAction("start")}>
            Pick up
          </PrimaryButton>
        ) : null}
        {ride.status === "InProgress" ? (
          <PrimaryButton data-testid="dropoff" disabled={busy} onClick={() => void onAction("dropoff")}>
            Drop off
          </PrimaryButton>
        ) : null}
      </div>
    );
  }

  return sheet;
}

import { formatEta, formatMinutes, initials, shortPlace } from "@/shared/format";
import { haversineMeters } from "@/features/maps/geo";
import type { RideView, DriverLocation } from "@/features/rides/types";
import { ErrorLine, TextButton } from "@/shared/ui";

export function AssignedSheet({
  ride,
  live,
  error,
  busy,
  onCancel,
}: {
  ride: RideView;
  live: DriverLocation | null;
  error: string | null;
  busy: boolean;
  onCancel: () => void;
}) {
  const driver = ride.driver;
  const title =
    ride.status === "Accepted"
      ? "Driver is on the way"
      : ride.status === "DriverArrived"
        ? "Driver has arrived"
        : `Heading to ${shortPlace(ride.dropoff.address)}`;
  const eta = riderEta(ride, live);
  return (
    <div className="stack">
      {driver ? (
        <section className="stack" data-testid="driver-card">
          <div className="title-row">
            <h1>{title}</h1>
            <div className="eta">{eta}</div>
          </div>
          <div className="who">
            <div className="avatar" aria-hidden="true">
              {initials(driver.name)}
            </div>
            <div>
              <div className="strong" translate="no">
                {driver.name}
              </div>
              <div className="muted">Motorbike</div>
            </div>
          </div>
          <div className="plate" translate="no">
            {driver.plate}
          </div>
        </section>
      ) : (
        <div className="title-row">
          <h1>{title}</h1>
          <div className="eta">{eta}</div>
        </div>
      )}
      <ErrorLine message={error} />
      {ride.status === "Accepted" || ride.status === "DriverArrived" ? (
        <TextButton data-testid="cancel-ride" disabled={busy} onClick={onCancel}>
          Cancel ride
        </TextButton>
      ) : null}
    </div>
  );
}

function riderEta(ride: RideView, live: DriverLocation | null): string {
  if (ride.status === "DriverArrived") return "Waiting";
  const position = live ?? (ride.driver ? { lat: ride.driver.lat, lng: ride.driver.lng } : null);
  if (!position) return formatMinutes(ride.durationS);
  const target = ride.status === "InProgress" ? ride.dropoff : ride.pickup;
  const speed = live?.speedKmh != null && live.speedKmh >= 5 ? live.speedKmh : 20;
  return formatEta(haversineMeters(position, target), speed);
}

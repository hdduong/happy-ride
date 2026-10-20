import type { RideView } from "@/features/rides/types";

export function RideBanner({ ride }: { ride: RideView | null }) {
  const toDropoff = ride?.status === "InProgress" || ride?.status === "DroppedOff";
  const banner =
    ride && (ride.status === "Accepted" || ride.status === "DriverArrived" || toDropoff) ? (
      <div className="banner">
        <div className="kicker">{toDropoff ? "Drop-off" : "Pickup"}</div>
        <div className="strong" translate="no">
          {(toDropoff ? ride.dropoff.address : ride.pickup.address) || (toDropoff ? "Drop-off" : "Pickup")}
        </div>
      </div>
    ) : null;

  return banner;
}

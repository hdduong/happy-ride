import { bearing, isPlotted, riderPosition, routeForStatus } from "@/features/maps/geo";
import type { MapMarker } from "@/features/maps/MapCanvas";
import type { DriverDashboard } from "./useDriverDashboard";

export function driverMap({ ride, live, offer, center, me }: Pick<DriverDashboard, "ride" | "live" | "offer" | "center" | "me">) {
  const driverLive = live && ride && live.rideId === ride.rideId ? live : null;
  const driverPoint = driverLive
    ? { lat: driverLive.lat, lng: driverLive.lng }
    : ride?.driver
      ? { lat: ride.driver.lat, lng: ride.driver.lng }
      : null;
  const route = routeForStatus(
    ride?.status,
    ride?.approachRoute,
    ride?.route,
    ride && isPlotted(ride.pickup) && isPlotted(ride.dropoff)
      ? [ride.pickup, ride.dropoff]
      : offer && isPlotted(offer.pickup) && isPlotted(offer.dropoff)
        ? [offer.pickup, offer.dropoff]
        : [],
    driverPoint,
  );
  const markers: MapMarker[] = [];
  const pickupPoint = ride?.pickup ?? offer?.pickup ?? null;
  const dropoffPoint = ride?.dropoff ?? offer?.dropoff ?? null;
  const together = ride?.status === "InProgress" || ride?.status === "DroppedOff";
  const riderPoint = pickupPoint ? riderPosition(ride?.status, pickupPoint, driverPoint) : null;
  if (riderPoint && isPlotted(riderPoint) && (ride || offer)) {
    markers.push({ ...riderPoint, kind: "pickup", offset: together ? [-16, 8] : undefined });
  }
  if (dropoffPoint && isPlotted(dropoffPoint) && (ride || offer)) markers.push({ ...dropoffPoint, kind: "dropoff" });
  if (driverPoint && ride && isPlotted(driverPoint)) {
    const target = ride.status === "Accepted" || ride.status === "DriverArrived" ? ride.pickup : ride.dropoff;
    const heading = driverLive?.heading != null ? driverLive.heading : bearing(driverPoint, target);
    markers.push({ ...driverPoint, kind: "bike", heading });
  }
  const self = me?.lat != null && me.lng != null ? { lat: me.lat, lng: me.lng } : null;
  if (!driverPoint && self && isPlotted(self)) markers.push({ ...self, kind: "bike" });

  return { center: pickupPoint && isPlotted(pickupPoint) ? pickupPoint : self && isPlotted(self) ? self : center, route, markers };
}
